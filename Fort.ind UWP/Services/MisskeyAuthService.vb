Imports Windows.Data.Json
Imports Windows.Security.Credentials
Imports Windows.Web.Http

''' <summary>
''' Signs the user in against the fort.social Misskey instance using MiAuth
''' (https://misskey-hub.net/en/docs/for-developers/api/token/miauth/) and stores the
''' resulting access token in the Windows credential vault.
'''
''' The consent page is opened in the user's default browser rather than
''' WebAuthenticationBroker's embedded web view - Sharkey/Misskey's frontend is a modern SPA
''' that hangs indefinitely in WAB's legacy embedded browser control. The app gets control
''' back via protocol activation: the callback URL uses a custom "fortind:" scheme registered
''' in the app manifest, so once fort.social redirects to it, Windows re-activates this app
''' with that URI (App.OnActivated), which we translate into a completed sign-in.
''' </summary>
Public Class MisskeyAuthService

    Public Const InstanceHost As String = "social.fort1nd.com"
    Private Const AppName As String = "Fort.ind"
    Private Const RequestedPermissions As String = "read:account"

    Private Const VaultResource As String = "Fort.ind.Misskey"
    Private Const VaultUsernameKey As String = "token"

    ''' <summary>
    ''' Must match the uap:Protocol Name registered in Package.appxmanifest.
    ''' </summary>
    Private Const CallbackScheme As String = "fortind"
    Private Const CallbackHost As String = "miauth-callback"
    Private Const CallbackSessionParam As String = "session"

    Private Shared ReadOnly s_lock As New Object()
    Private Shared s_pendingSession As String = Nothing
    Private Shared s_pendingCompletion As TaskCompletionSource(Of Boolean) = Nothing

    ''' <summary>
    ''' Opens the fort.social consent page in the system browser, then waits for the browser
    ''' to redirect back into the app (via protocol activation) before exchanging the approved
    ''' session for an access token. Times out if the user never completes the browser flow.
    ''' </summary>
    Public Shared Async Function SignInAsync() As Task(Of MisskeyAuthResult)
        Dim session = Guid.NewGuid().ToString()
        Dim callbackUri As New Uri($"{CallbackScheme}://{CallbackHost}?{CallbackSessionParam}={session}")

        Dim startUri As New Uri(
            $"https://{InstanceHost}/miauth/{session}" &
            $"?name={Uri.EscapeDataString(AppName)}" &
            $"&callback={Uri.EscapeDataString(callbackUri.ToString())}" &
            $"&permission={RequestedPermissions}")

        Dim completion As New TaskCompletionSource(Of Boolean)
        SyncLock s_lock
            s_pendingSession = session
            s_pendingCompletion = completion
        End SyncLock

        Try
            Dim launched = Await Windows.System.Launcher.LaunchUriAsync(startUri)
            If Not launched Then
                ClearPending(session)
                Return MisskeyAuthResult.Failed("Could not open your browser to sign in.")
            End If
        Catch ex As Exception
            ClearPending(session)
            Debug.WriteLine($"MisskeyAuthService: launch failed - {ex.Message}")
            Return MisskeyAuthResult.Failed("Could not open your browser to sign in.")
        End Try

        Dim finished = Await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMinutes(5)))
        If finished IsNot completion.Task Then
            ClearPending(session)
            Return MisskeyAuthResult.Failed("Sign-in timed out. Please try again.")
        End If

        Dim approved = Await completion.Task
        If Not approved Then
            Return MisskeyAuthResult.Failed("Sign-in was cancelled.")
        End If

        Return Await CompleteSessionAsync(session)
    End Function

    ''' <summary>
    ''' Call from App.OnActivated when the app is reactivated via the "fortind:" protocol.
    ''' If a SignInAsync call is still waiting in this process, this simply unblocks it (it
    ''' will do the token exchange itself). Otherwise - e.g. the app was suspended/restarted
    ''' while the user was in the browser - the session is recovered from the callback URL's
    ''' query string and the exchange is completed directly, returning the result so the
    ''' caller (App.OnActivated) can apply it.
    ''' </summary>
    Public Shared Async Function HandleProtocolActivationAsync(uri As Uri) As Task(Of MisskeyAuthResult)
        Dim completion As TaskCompletionSource(Of Boolean) = Nothing
        SyncLock s_lock
            completion = s_pendingCompletion
            s_pendingSession = Nothing
            s_pendingCompletion = Nothing
        End SyncLock

        If completion IsNot Nothing Then
            completion.TrySetResult(True)
            Return Nothing
        End If

        Dim session = ExtractSessionFromCallback(uri)
        If String.IsNullOrWhiteSpace(session) Then
            Return MisskeyAuthResult.Failed("Sign-in link was missing session information.")
        End If

        Return Await CompleteSessionAsync(session)
    End Function

    ''' <summary>
    ''' Cancels an in-flight sign-in (e.g. the user gives up while stuck in the browser).
    ''' </summary>
    Public Shared Sub CancelPendingSignIn()
        Dim completion As TaskCompletionSource(Of Boolean) = Nothing
        SyncLock s_lock
            completion = s_pendingCompletion
            s_pendingSession = Nothing
            s_pendingCompletion = Nothing
        End SyncLock
        completion?.TrySetResult(False)
    End Sub

    Private Shared Sub ClearPending(session As String)
        SyncLock s_lock
            If s_pendingSession = session Then
                s_pendingSession = Nothing
                s_pendingCompletion = Nothing
            End If
        End SyncLock
    End Sub

    Private Shared Function ExtractSessionFromCallback(uri As Uri) As String
        Try
            If uri Is Nothing OrElse String.IsNullOrEmpty(uri.Query) Then Return Nothing
            Dim decoder As New Windows.Foundation.WwwFormUrlDecoder(uri.Query)
            For Each entry In decoder
                If String.Equals(entry.Name, CallbackSessionParam, StringComparison.OrdinalIgnoreCase) Then
                    Return entry.Value
                End If
            Next
        Catch ex As Exception
            Debug.WriteLine($"MisskeyAuthService: failed to parse callback URI - {ex.Message}")
        End Try
        Return Nothing
    End Function

    ''' <summary>
    ''' Exchanges an approved MiAuth session for an access token, per
    ''' POST /api/miauth/{session}/check.
    ''' </summary>
    Private Shared Async Function CompleteSessionAsync(session As String) As Task(Of MisskeyAuthResult)
        Try
            Using client As New HttpClient()
                Dim checkUri As New Uri($"https://{InstanceHost}/api/miauth/{session}/check")
                Dim content = New HttpStringContent("{}", Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json")
                Dim response = Await client.PostAsync(checkUri, content)
                response.EnsureSuccessStatusCode()

                Dim body = Await response.Content.ReadAsStringAsync()
                Dim json = JsonObject.Parse(body)

                If Not json.GetNamedBoolean("ok", False) Then
                    Return MisskeyAuthResult.Failed("fort.social did not approve the sign-in.")
                End If

                Dim token = json.GetNamedString("token", "")
                If String.IsNullOrWhiteSpace(token) Then
                    Return MisskeyAuthResult.Failed("fort.social did not return an access token.")
                End If

                Dim profile = ParseUser(GetNamedObjectOrNull(json, "user"))
                If profile Is Nothing Then
                    Return MisskeyAuthResult.Failed("fort.social did not return account details.")
                End If

                SaveToken(token)
                Return MisskeyAuthResult.Succeeded(token, profile)
            End Using
        Catch ex As Exception
            Debug.WriteLine($"MisskeyAuthService: check failed - {ex.Message}")
            Return MisskeyAuthResult.Failed("Could not reach fort.social.")
        End Try
    End Function

    ''' <summary>
    ''' Re-fetches the signed-in user's profile using a previously issued token, per POST /api/i.
    ''' Returns Nothing if the token is missing, invalid, or the instance is unreachable.
    ''' </summary>
    Public Shared Async Function FetchCurrentUserAsync(token As String) As Task(Of UserProfile)
        If String.IsNullOrWhiteSpace(token) Then Return Nothing

        Try
            Using client As New HttpClient()
                Dim uri As New Uri($"https://{InstanceHost}/api/i")
                Dim bodyJson As New JsonObject()
                bodyJson.Add("i", JsonValue.CreateStringValue(token))
                Dim content = New HttpStringContent(bodyJson.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json")

                Dim response = Await client.PostAsync(uri, content)
                If Not response.IsSuccessStatusCode Then Return Nothing

                Dim body = Await response.Content.ReadAsStringAsync()
                Return ParseUser(JsonObject.Parse(body))
            End Using
        Catch ex As Exception
            Debug.WriteLine($"MisskeyAuthService: /api/i failed - {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Builds a UserProfile from a Misskey user JSON object (works for both the "user" object
    ''' nested in the MiAuth check response and the root object returned by /api/i).
    ''' </summary>
    Private Shared Function ParseUser(obj As JsonObject) As UserProfile
        If obj Is Nothing Then Return Nothing

        Dim id = JsonString(obj, "id")
        Dim username = JsonString(obj, "username")
        If String.IsNullOrWhiteSpace(id) OrElse String.IsNullOrWhiteSpace(username) Then Return Nothing

        Dim profile As New UserProfile()
        profile.UserId = id
        profile.Username = username
        profile.Host = JsonString(obj, "host")
        profile.DisplayName = JsonString(obj, "name")
        profile.Bio = JsonString(obj, "description")
        profile.AvatarUrl = JsonString(obj, "avatarUrl")
        profile.LastLoginDate = DateTime.Now

        Dim createdAt = JsonString(obj, "createdAt")
        Dim parsedDate As DateTime
        If Not String.IsNullOrWhiteSpace(createdAt) AndAlso
           DateTime.TryParse(createdAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, parsedDate) Then
            profile.CreatedDate = parsedDate
        End If

        Return profile
    End Function

    ''' <summary>
    ''' Reads a string field, tolerating a missing key or a JSON null (both routinely occur
    ''' for fields like "host" or "name" on Misskey accounts).
    ''' </summary>
    Private Shared Function JsonString(obj As JsonObject, key As String) As String
        If obj Is Nothing OrElse Not obj.ContainsKey(key) Then Return Nothing
        Dim v = obj.GetNamedValue(key)
        If v.ValueType <> JsonValueType.String Then Return Nothing
        Return v.GetString()
    End Function

    Private Shared Function GetNamedObjectOrNull(obj As JsonObject, key As String) As JsonObject
        If obj Is Nothing OrElse Not obj.ContainsKey(key) Then Return Nothing
        If obj.GetNamedValue(key).ValueType <> JsonValueType.Object Then Return Nothing
        Return obj.GetNamedObject(key)
    End Function

#Region "Token Storage"

    ''' <summary>
    ''' Persists the access token in the Windows credential vault, replacing any existing one.
    ''' </summary>
    Private Shared Sub SaveToken(token As String)
        ClearToken()
        Dim vault As New PasswordVault()
        vault.Add(New PasswordCredential(VaultResource, VaultUsernameKey, token))
    End Sub

    ''' <summary>
    ''' Retrieves the stored access token, or Nothing if the user isn't signed in.
    ''' PasswordVault throws (rather than returning Nothing) when no credential is stored,
    ''' so a missing token is treated as the normal "not signed in" case.
    ''' </summary>
    Public Shared Function TryGetToken() As String
        Try
            Dim vault As New PasswordVault()
            Dim credential = vault.Retrieve(VaultResource, VaultUsernameKey)
            credential.RetrievePassword()
            Return credential.Password
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Removes the stored access token, if any.
    ''' </summary>
    Public Shared Sub ClearToken()
        Try
            Dim vault As New PasswordVault()
            Dim credential = vault.Retrieve(VaultResource, VaultUsernameKey)
            vault.Remove(credential)
        Catch
            ' Nothing stored - already signed out.
        End Try
    End Sub

#End Region

End Class

''' <summary>
''' Result of a MiAuth sign-in attempt.
''' </summary>
Public Class MisskeyAuthResult
    Public Property Success As Boolean
    Public Property ErrorMessage As String
    Public Property Token As String
    Public Property Profile As UserProfile

    Private Sub New()
    End Sub

    Public Shared Function Failed(message As String) As MisskeyAuthResult
        Return New MisskeyAuthResult With {.Success = False, .ErrorMessage = message}
    End Function

    Public Shared Function Succeeded(token As String, profile As UserProfile) As MisskeyAuthResult
        Return New MisskeyAuthResult With {.Success = True, .Token = token, .Profile = profile}
    End Function
End Class
