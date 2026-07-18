Imports Windows.UI.Xaml.Media.Animation
Imports Windows.UI.Xaml.Media.Imaging

''' <summary>
''' Profile viewing page. Read-only: the fort.social account is the source of truth,
''' so editing happens on the instance, not here.
''' </summary>
Public NotInheritable Class ProfilePage
    Inherits Page

    ' Guard to prevent multiple ContentDialogs from opening simultaneously
    Private _dialogSemaphore As New Threading.SemaphoreSlim(1, 1)

    Public Sub New()
        Me.InitializeComponent()
        AddHandler Loaded, AddressOf ProfilePage_Loaded
        AddHandler Unloaded, AddressOf ProfilePage_Unloaded
        AddHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
    End Sub

    Private Sub ProfilePage_Unloaded(sender As Object, e As RoutedEventArgs)
        RemoveHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
    End Sub

    Private Async Sub OnAuthStateChanged(sender As Object, isLoggedIn As Boolean)
        Try
            Await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                Sub()
                    Try
                        RefreshUI()
                    Catch ex As Exception
                        Debug.WriteLine($"ProfilePage: RefreshUI failed - {ex.Message}")
                    End Try
                End Sub)
        Catch ex As Exception
            ' Critical: Catch exceptions in async void to prevent app crash
            Debug.WriteLine($"ProfilePage: Auth state change handler failed - {ex.Message}")
        End Try
    End Sub

    Private Sub ProfilePage_Loaded(sender As Object, e As RoutedEventArgs)
        RefreshUI()
    End Sub

    ''' <summary>
    ''' Refresh the UI based on login state
    ''' </summary>
    Public Sub RefreshUI()
        If ProfileService.CurrentUser IsNot Nothing Then
            ShowLoggedInState()
        Else
            ShowNotLoggedInState()
        End If
    End Sub

    Private Sub ShowLoggedInState()
        NotLoggedInPanel.Visibility = Visibility.Collapsed
        LoggedInPanel.Visibility = Visibility.Visible

        Dim user = ProfileService.CurrentUser
        Dim host = If(String.IsNullOrWhiteSpace(user.Host), MisskeyAuthService.InstanceHost, user.Host)

        ' Update profile header
        DisplayNameText.Text = If(String.IsNullOrWhiteSpace(user.DisplayName), user.Username, user.DisplayName)
        UsernameText.Text = $"@{user.Username}@{host}"

        If user.CreatedDate > DateTime.MinValue Then
            MemberSinceText.Text = $"Member since {user.CreatedDate:MMMM yyyy}"
            MemberSinceText.Visibility = Visibility.Visible
        Else
            MemberSinceText.Visibility = Visibility.Collapsed
        End If

        If user.LastLoginDate > DateTime.MinValue Then
            LastLoginText.Text = $"Last signed in: {user.LastLoginDate:MMM d, yyyy h:mm tt}"
            LastLoginText.Visibility = Visibility.Visible
        Else
            LastLoginText.Visibility = Visibility.Collapsed
        End If

        ' Set initials (up to two letters: first letter of each word)
        Dim name = If(String.IsNullOrWhiteSpace(user.DisplayName), user.Username, user.DisplayName)
        ProfileInitials.Text = GetInitials(name)
        UpdateAvatarUI(user.AvatarUrl)

        ' Update bio
        BioText.Text = If(String.IsNullOrWhiteSpace(user.Bio), "No bio set", user.Bio)

        ' Fade in the avatar
        Dim sb = TryCast(Me.Resources("AvatarFadeIn"), Storyboard)
        sb?.Begin()
    End Sub

    Private Sub ShowNotLoggedInState()
        NotLoggedInPanel.Visibility = Visibility.Visible
        LoggedInPanel.Visibility = Visibility.Collapsed
    End Sub

    Private Sub SignInButton_Click(sender As Object, e As RoutedEventArgs)
        Frame.Navigate(GetType(LoginPage))
    End Sub

    Private Async Sub ManageOnFortSocialButton_Click(sender As Object, e As RoutedEventArgs)
        Dim user = ProfileService.CurrentUser
        If user Is Nothing Then Return

        Try
            Dim host = If(String.IsNullOrWhiteSpace(user.Host), MisskeyAuthService.InstanceHost, user.Host)
            Dim uri As New Uri($"https://{host}/@{user.Username}")
            Await Windows.System.Launcher.LaunchUriAsync(uri)
        Catch ex As Exception
            Debug.WriteLine($"ProfilePage: Failed to open fort.social profile - {ex.Message}")
        End Try
    End Sub

    Private Async Sub LogoutButton_Click(sender As Object, e As RoutedEventArgs)
        ' Use semaphore to prevent concurrent dialog opening
        If Not Await _dialogSemaphore.WaitAsync(0) Then
            Return ' Another dialog is already open
        End If

        Try
            Dim dialog As New ContentDialog()
            dialog.Title = "Sign Out"
            dialog.Content = "Are you sure you want to sign out?"
            dialog.PrimaryButtonText = "Sign Out"
            dialog.CloseButtonText = "Cancel"
            dialog.DefaultButton = ContentDialogButton.Close
            dialog.XamlRoot = Me.XamlRoot

            Dim result = Await dialog.ShowAsync()

            If result = ContentDialogResult.Primary Then
                Await ProfileService.LogoutAsync()
                RefreshUI()
            End If
        Catch ex As Exception
            Debug.WriteLine($"ProfilePage: Logout dialog failed – {ex.Message}")
        Finally
            _dialogSemaphore.Release()
        End Try
    End Sub

    Private Function GetInitials(name As String) As String
        If String.IsNullOrWhiteSpace(name) Then
            Return "?"
        End If

        Dim parts = name.Trim().Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
        If parts.Length >= 2 AndAlso parts(1).Length > 0 Then
            Return (parts(0).Substring(0, 1) & parts(1).Substring(0, 1)).ToUpper()
        End If

        Return parts(0).Substring(0, 1).ToUpper()
    End Function

    Private Sub UpdateAvatarUI(avatarUrl As String)
        Try
            If String.IsNullOrWhiteSpace(avatarUrl) Then
                ProfileImage.Source = Nothing
                ProfileImage.Visibility = Visibility.Collapsed
                ProfileInitials.Visibility = Visibility.Visible
                Return
            End If

            Dim bitmap As New BitmapImage()
            bitmap.UriSource = New Uri(avatarUrl)
            ProfileImage.Source = bitmap
            ProfileImage.Visibility = Visibility.Visible
            ProfileInitials.Visibility = Visibility.Collapsed
        Catch ex As Exception
            Debug.WriteLine($"ProfilePage: Avatar load failed - {ex.Message}")
            ProfileImage.Source = Nothing
            ProfileImage.Visibility = Visibility.Collapsed
            ProfileInitials.Visibility = Visibility.Visible
        End Try
    End Sub

End Class
