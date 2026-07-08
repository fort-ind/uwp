Imports Windows.Storage
Imports System.Runtime.Serialization
Imports System.Runtime.Serialization.Json
Imports System.IO
Imports System.Threading

''' <summary>
''' Service for storing and retrieving data locally
''' Uses Windows.Storage.ApplicationData for UWP-safe storage
''' Can be extended to sync with server later
''' </summary>
Public Class LocalStorageService

    Private Shared ReadOnly LocalFolder As StorageFolder = ApplicationData.Current.LocalFolder
    Private Const PROFILES_FOLDER As String = "Profiles"
    Private Const CURRENT_USER_FILE As String = "current_user.json"
    Private Const SETTINGS_FILE As String = "app_settings.json"
    Private Const USERNAME_INDEX_FILE As String = "username_index.json"

    ' Serializes access to the username index + user-count cache so concurrent
    ' register/delete operations cannot corrupt them or race on the count.
    Private Shared ReadOnly s_indexLock As New SemaphoreSlim(1, 1)

    ' In-memory username(lowercase) -> userId map, lazily loaded from USERNAME_INDEX_FILE.
    ' Nothing until first load; guarded by s_indexLock.
    Private Shared s_usernameIndex As Dictionary(Of String, String) = Nothing

    ' Cached registered-user count (-1 = not yet computed). Guarded by s_indexLock.
    Private Shared s_userCountCache As Integer = -1

    ''' <summary>
    ''' Storage mode for user data (future: SQLite, Server)
    ''' </summary>
    Public Enum StorageMode
        JsonFile = 0
        ' SQLite = 1  ' Coming soon - requires server setup
        ' Server = 2  ' Coming soon - requires API backend
    End Enum

    ''' <summary>
    ''' Current storage mode
    ''' </summary>
    Public Shared Property CurrentStorageMode As StorageMode = StorageMode.JsonFile

    ''' <summary>
    ''' Initializes storage service
    ''' </summary>
    Public Shared Function InitializeAsync() As Task
        ' Future: Load storage mode from settings
        CurrentStorageMode = StorageMode.JsonFile
        Return Task.CompletedTask
    End Function

#Region "Profile Operations"

    ''' <summary>
    ''' Saves a user profile to local storage as a JSON file :p (temporarily) until we implement a more secure storage solution like SQLite or server-side storage with encryption.
    ''' </summary>
    Public Shared Async Function SaveProfileAsync(profile As UserProfile, Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)
        If profile Is Nothing OrElse String.IsNullOrWhiteSpace(profile.UserId) Then
            Return False
        End If

        Try
            cancellationToken.ThrowIfCancellationRequested()

            ' Ensure profiles folder exists
            Dim profilesFolder = Await LocalFolder.CreateFolderAsync(PROFILES_FOLDER, CreationCollisionOption.OpenIfExists)

            cancellationToken.ThrowIfCancellationRequested()

            ' Create file for this user
            Dim fileName = $"{profile.UserId}.json"
            Dim file = Await profilesFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting)

            ' Serialize and save
            Dim json = SerializeToJson(profile)
            Await FileIO.WriteTextAsync(file, json).AsTask(cancellationToken)

            ' Keep the username index in sync (also handles username changes).
            Await UpdateIndexAsync(profile.Username, profile.UserId)

            Return True
        Catch ex As OperationCanceledException
            Debug.WriteLine("Profile save operation was cancelled")
            Return False
        Catch ex As Exception
            Debug.WriteLine($"Error saving profile: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Loads a user profile by user ID
    ''' </summary>
    Public Shared Async Function LoadProfileAsync(userId As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of UserProfile)
        If String.IsNullOrWhiteSpace(userId) Then Return Nothing

        Try
            cancellationToken.ThrowIfCancellationRequested()

            Dim profilesFolder = Await LocalFolder.GetFolderAsync(PROFILES_FOLDER)
            Dim fileName = $"{userId}.json"
            Dim file = Await profilesFolder.GetFileAsync(fileName)

            cancellationToken.ThrowIfCancellationRequested()

            Dim json = Await FileIO.ReadTextAsync(file).AsTask(cancellationToken)
            Return DeserializeFromJson(Of UserProfile)(json)
        Catch ex As OperationCanceledException
            Debug.WriteLine("Profile load operation was cancelled")
            Return Nothing
        Catch ex As Exception
            Debug.WriteLine($"Error loading profile: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Loads a user profile by username. Uses the O(1) username index to resolve the userId
    ''' and then loads that profile directly, falling back to a full scan only if the index
    ''' misses (e.g. first run before migration, or a stale index).
    ''' </summary>
    Public Shared Async Function LoadProfileByUsernameAsync(username As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of UserProfile)
        If String.IsNullOrWhiteSpace(username) Then Return Nothing

        Try
            cancellationToken.ThrowIfCancellationRequested()

            ' Fast path: resolve via the index and load a single file.
            Dim indexedId = Await LookupUserIdByUsernameAsync(username)
            If Not String.IsNullOrWhiteSpace(indexedId) Then
                Dim indexed = Await LoadProfileAsync(indexedId, cancellationToken)
                ' Confirm the index wasn't stale before trusting it.
                If indexed IsNot Nothing AndAlso String.Equals(indexed.Username, username, StringComparison.OrdinalIgnoreCase) Then
                    Return indexed
                End If
            End If

            ' Slow path: scan files (also refreshes the index for next time).
            Return Await ScanForProfileByUsernameAsync(username, cancellationToken)
        Catch ex As OperationCanceledException
            Debug.WriteLine("Profile search operation was cancelled")
            Return Nothing
        Catch ex As Exception
            Debug.WriteLine($"Error loading profile by username: {ex.Message}")
        End Try

        Return Nothing
    End Function

    ''' <summary>
    ''' Full-scan fallback that locates a profile by username and, on success, refreshes
    ''' the username index so subsequent lookups take the fast path.
    ''' </summary>
    Private Shared Async Function ScanForProfileByUsernameAsync(username As String, cancellationToken As CancellationToken) As Task(Of UserProfile)
        Dim profilesFolder = Await LocalFolder.CreateFolderAsync(PROFILES_FOLDER, CreationCollisionOption.OpenIfExists)
        Dim files = Await profilesFolder.GetFilesAsync()

        For Each file In files
            cancellationToken.ThrowIfCancellationRequested()

            If file.Name.EndsWith(".json") Then
                Try
                    Dim json = Await FileIO.ReadTextAsync(file).AsTask(cancellationToken)
                    Dim profile = DeserializeFromJson(Of UserProfile)(json)
                    If profile IsNot Nothing AndAlso String.Equals(profile.Username, username, StringComparison.OrdinalIgnoreCase) Then
                        ' Heal the index so the next lookup is O(1).
                        Await UpdateIndexAsync(profile.Username, profile.UserId)
                        Return profile
                    End If
                Catch
                    ' Skip corrupt files
                End Try
            End If
        Next

        Return Nothing
    End Function

    ''' <summary>
    ''' Gets all stored profiles
    ''' </summary>
    Public Shared Async Function GetAllProfilesAsync() As Task(Of List(Of UserProfile))
        Dim profiles As New List(Of UserProfile)

        Try
            Dim profilesFolder = Await LocalFolder.CreateFolderAsync(PROFILES_FOLDER, CreationCollisionOption.OpenIfExists)
            Dim files = Await profilesFolder.GetFilesAsync()

            For Each file In files
                If file.Name.EndsWith(".json") Then
                    Try
                        Dim json = Await FileIO.ReadTextAsync(file)
                        Dim profile = DeserializeFromJson(Of UserProfile)(json)
                        If profile IsNot Nothing Then
                            profiles.Add(profile)
                        End If
                    Catch ex As Exception
                        Debug.WriteLine($"Skipping corrupt profile '{file.Name}': {ex.Message}")
                    End Try
                End If
            Next
        Catch ex As Exception
            Debug.WriteLine($"Error getting all profiles: {ex.Message}")
        End Try

        Return profiles
    End Function

    ''' <summary>
    ''' Deletes a user profile
    ''' </summary>
    Public Shared Async Function DeleteProfileAsync(userId As String) As Task(Of Boolean)
        If String.IsNullOrWhiteSpace(userId) Then Return False

        Try
            Dim profilesFolder = Await LocalFolder.GetFolderAsync(PROFILES_FOLDER)
            Dim fileName = $"{userId}.json"
            Dim file = Await profilesFolder.GetFileAsync(fileName)
            Await file.DeleteAsync()

            ' Drop the username -> userId mapping(s) for the deleted user.
            Await RemoveFromIndexAsync(userId)

            ' Clear the session file if the deleted user was logged in
            Dim currentId = Await GetCurrentUserIdAsync()
            If currentId = userId Then
                Await ClearCurrentUserAsync()
            End If

            Return True
        Catch ex As Exception
            Debug.WriteLine($"Error deleting profile: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Checks if a username is already taken. Resolves via the O(1) index; only touches disk
    ''' to confirm when the index reports a hit (guards against a stale entry) or misses on a
    ''' not-yet-migrated install.
    ''' </summary>
    Public Shared Async Function IsUsernameTakenAsync(username As String) As Task(Of Boolean)
        If String.IsNullOrWhiteSpace(username) Then Return False

        Dim indexedId = Await LookupUserIdByUsernameAsync(username)
        If Not String.IsNullOrWhiteSpace(indexedId) Then
            ' Confirm the mapping still resolves to a real profile with this username.
            Dim profile = Await LoadProfileAsync(indexedId)
            If profile IsNot Nothing AndAlso String.Equals(profile.Username, username, StringComparison.OrdinalIgnoreCase) Then
                Return True
            End If
        End If

        ' Index miss – fall back to a scan (which heals the index if it finds a match).
        Dim scanned = Await ScanForProfileByUsernameAsync(username, CancellationToken.None)
        Return scanned IsNot Nothing
    End Function

#End Region

#Region "Session Management"

    ''' <summary>
    ''' Saves the current logged-in user ID
    ''' </summary>
    Public Shared Async Function SaveCurrentUserIdAsync(userId As String) As Task
        If String.IsNullOrWhiteSpace(userId) Then Return

        Try
            Dim file = Await LocalFolder.CreateFileAsync(CURRENT_USER_FILE, CreationCollisionOption.ReplaceExisting)
            Await FileIO.WriteTextAsync(file, userId)
        Catch ex As Exception
            Debug.WriteLine($"Error saving current user: {ex.Message}")
        End Try
    End Function

    ''' <summary>
    ''' Gets the current logged-in user ID
    ''' </summary>
    Public Shared Async Function GetCurrentUserIdAsync() As Task(Of String)
        Try
            Dim file = Await LocalFolder.GetFileAsync(CURRENT_USER_FILE)
            Dim id = (Await FileIO.ReadTextAsync(file))?.Trim()
            If String.IsNullOrWhiteSpace(id) Then Return Nothing
            Return id
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Clears the current user session
    ''' </summary>
    Public Shared Async Function ClearCurrentUserAsync() As Task
        Try
            Dim file = Await LocalFolder.GetFileAsync(CURRENT_USER_FILE)
            Await file.DeleteAsync()
        Catch
            ' File doesn't exist, that's fine
        End Try
    End Function

#End Region

#Region "Settings"

    ''' <summary>
    ''' Gets the data storage location path
    ''' </summary>
    Public Shared ReadOnly Property DataPath As String
        Get
            Return LocalFolder.Path
        End Get
    End Property

    ''' <summary>
    ''' Gets the number of registered users. Served from the cached index count (kept in sync
    ''' on register/delete), so opening Settings no longer re-enumerates the profiles folder.
    ''' </summary>
    Public Shared Async Function GetUserCountAsync() As Task(Of Integer)
        Try
            Await s_indexLock.WaitAsync()
            Try
                Await EnsureIndexLoadedAsync()
                Return If(s_userCountCache >= 0, s_userCountCache, 0)
            Finally
                s_indexLock.Release()
            End Try
        Catch ex As Exception
            Debug.WriteLine($"Error getting user count: {ex.Message}")
            Return 0
        End Try
    End Function

#End Region

#Region "Username Index"

    ''' <summary>
    ''' Ensures the in-memory username index is loaded. Loads from the index file if present,
    ''' otherwise rebuilds it by scanning profiles once (migrates existing installs).
    ''' Caller must hold s_indexLock.
    ''' </summary>
    Private Shared Async Function EnsureIndexLoadedAsync() As Task
        If s_usernameIndex IsNot Nothing Then Return

        ' Try to load the persisted index.
        Try
            Dim indexFile = Await LocalFolder.GetFileAsync(USERNAME_INDEX_FILE)
            Dim json = Await FileIO.ReadTextAsync(indexFile)
            Dim loaded = DeserializeFromJson(Of UsernameIndex)(json)
            If loaded IsNot Nothing AndAlso loaded.Entries IsNot Nothing Then
                Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                For Each entry In loaded.Entries
                    If Not String.IsNullOrWhiteSpace(entry.Username) AndAlso Not String.IsNullOrWhiteSpace(entry.UserId) Then
                        map(entry.Username) = entry.UserId
                    End If
                Next
                s_usernameIndex = map
                s_userCountCache = map.Count
                Return
            End If
        Catch ex As Exception
            ' Missing or corrupt index file – fall through to a rebuild.
            Debug.WriteLine($"Username index load failed, rebuilding: {ex.Message}")
        End Try

        Await RebuildIndexAsync()
    End Function

    ''' <summary>
    ''' Rebuilds the username index from a full profile scan and persists it.
    ''' Caller must hold s_indexLock.
    ''' </summary>
    Private Shared Async Function RebuildIndexAsync() As Task
        Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            Dim profilesFolder = Await LocalFolder.CreateFolderAsync(PROFILES_FOLDER, CreationCollisionOption.OpenIfExists)
            Dim files = Await profilesFolder.GetFilesAsync()
            For Each file In files
                If Not file.Name.EndsWith(".json") Then Continue For
                Try
                    Dim json = Await FileIO.ReadTextAsync(file)
                    Dim profile = DeserializeFromJson(Of UserProfile)(json)
                    If profile IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(profile.Username) AndAlso Not String.IsNullOrWhiteSpace(profile.UserId) Then
                        map(profile.Username) = profile.UserId
                    End If
                Catch
                    ' Skip corrupt files
                End Try
            Next
        Catch ex As Exception
            Debug.WriteLine($"Username index rebuild failed: {ex.Message}")
        End Try

        s_usernameIndex = map
        s_userCountCache = map.Count
        Await PersistIndexAsync()
    End Function

    ''' <summary>
    ''' Writes the in-memory index to disk. Caller must hold s_indexLock.
    ''' </summary>
    Private Shared Async Function PersistIndexAsync() As Task
        If s_usernameIndex Is Nothing Then Return
        Try
            Dim snapshot As New UsernameIndex()
            For Each kvp In s_usernameIndex
                snapshot.Entries.Add(New UsernameIndexEntry With {.Username = kvp.Key, .UserId = kvp.Value})
            Next
            Dim json = SerializeToJson(snapshot)
            Dim indexFile = Await LocalFolder.CreateFileAsync(USERNAME_INDEX_FILE, CreationCollisionOption.ReplaceExisting)
            Await FileIO.WriteTextAsync(indexFile, json)
        Catch ex As Exception
            Debug.WriteLine($"Username index persist failed: {ex.Message}")
        End Try
    End Function

    ''' <summary>
    ''' Adds/updates a username -> userId mapping and refreshes the count cache.
    ''' Removes any stale username entries that pointed at the same userId (username change).
    ''' </summary>
    Private Shared Async Function UpdateIndexAsync(username As String, userId As String) As Task
        If String.IsNullOrWhiteSpace(username) OrElse String.IsNullOrWhiteSpace(userId) Then Return
        Await s_indexLock.WaitAsync()
        Try
            Await EnsureIndexLoadedAsync()

            ' Drop any other username currently mapped to this userId (e.g. renamed account).
            Dim stale = s_usernameIndex.Where(Function(kvp) String.Equals(kvp.Value, userId, StringComparison.Ordinal) AndAlso
                                                           Not String.Equals(kvp.Key, username, StringComparison.OrdinalIgnoreCase)).
                                        Select(Function(kvp) kvp.Key).ToList()
            For Each key In stale
                s_usernameIndex.Remove(key)
            Next

            s_usernameIndex(username) = userId
            s_userCountCache = s_usernameIndex.Count
            Await PersistIndexAsync()
        Finally
            s_indexLock.Release()
        End Try
    End Function

    ''' <summary>
    ''' Removes all index entries pointing at the given userId and refreshes the count cache.
    ''' </summary>
    Private Shared Async Function RemoveFromIndexAsync(userId As String) As Task
        If String.IsNullOrWhiteSpace(userId) Then Return
        Await s_indexLock.WaitAsync()
        Try
            Await EnsureIndexLoadedAsync()
            Dim keys = s_usernameIndex.Where(Function(kvp) String.Equals(kvp.Value, userId, StringComparison.Ordinal)).
                                       Select(Function(kvp) kvp.Key).ToList()
            For Each key In keys
                s_usernameIndex.Remove(key)
            Next
            s_userCountCache = s_usernameIndex.Count
            Await PersistIndexAsync()
        Finally
            s_indexLock.Release()
        End Try
    End Function

    ''' <summary>
    ''' Resolves a username to its userId via the index (O(1)), or Nothing if not found.
    ''' </summary>
    Private Shared Async Function LookupUserIdByUsernameAsync(username As String) As Task(Of String)
        If String.IsNullOrWhiteSpace(username) Then Return Nothing
        Await s_indexLock.WaitAsync()
        Try
            Await EnsureIndexLoadedAsync()
            Dim userId As String = Nothing
            If s_usernameIndex.TryGetValue(username, userId) Then
                Return userId
            End If
            Return Nothing
        Finally
            s_indexLock.Release()
        End Try
    End Function

#End Region

#Region "JSON Serialization"

    ' Cache serializers to avoid repeated reflection on each call
    Private Shared ReadOnly s_serializerCache As New Concurrent.ConcurrentDictionary(Of Type, DataContractJsonSerializer)

    Private Shared Function GetSerializer(t As Type) As DataContractJsonSerializer
        Return s_serializerCache.GetOrAdd(t, Function(key) New DataContractJsonSerializer(key))
    End Function

    Private Shared Function SerializeToJson(Of T)(obj As T) As String
        Using stream As New MemoryStream()
            Dim serializer = GetSerializer(GetType(T))
            serializer.WriteObject(stream, obj)
            stream.Position = 0
            Using reader As New StreamReader(stream)
                Return reader.ReadToEnd()
            End Using
        End Using
    End Function

    Private Shared Function DeserializeFromJson(Of T)(json As String) As T
        If String.IsNullOrWhiteSpace(json) Then Return Nothing
        Using stream As New MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))
            Dim serializer = GetSerializer(GetType(T))
            Return CType(serializer.ReadObject(stream), T)
        End Using
    End Function

#End Region

End Class

''' <summary>
''' Serializable container for the persisted username -> userId index.
''' </summary>
<DataContract>
Public Class UsernameIndex
    <DataMember>
    Public Property Entries As List(Of UsernameIndexEntry)
        Get
            If _entries Is Nothing Then _entries = New List(Of UsernameIndexEntry)()
            Return _entries
        End Get
        Set(value As List(Of UsernameIndexEntry))
            _entries = value
        End Set
    End Property
    Private _entries As List(Of UsernameIndexEntry)
End Class

''' <summary>
''' A single username -> userId mapping entry.
''' </summary>
<DataContract>
Public Class UsernameIndexEntry
    <DataMember>
    Public Property Username As String
    <DataMember>
    Public Property UserId As String
End Class
