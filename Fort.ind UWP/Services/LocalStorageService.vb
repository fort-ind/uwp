Imports Windows.Storage
Imports System.Runtime.Serialization
Imports System.Runtime.Serialization.Json
Imports System.IO
Imports System.Threading

''' <summary>
''' Caches the signed-in fort.social profile locally so the UI has something to show
''' immediately at startup, before (or without) a network round-trip.
''' Uses Windows.Storage.ApplicationData for UWP-safe storage.
''' </summary>
Public Class LocalStorageService

    Private Shared ReadOnly LocalFolder As StorageFolder = ApplicationData.Current.LocalFolder
    Private Const PROFILE_FILE As String = "misskey_profile.json"

    ''' <summary>
    ''' Initializes storage service
    ''' </summary>
    Public Shared Function InitializeAsync() As Task
        Return Task.CompletedTask
    End Function

#Region "Profile Cache"

    ''' <summary>
    ''' Caches the signed-in user's profile as a local JSON file.
    ''' </summary>
    Public Shared Async Function SaveProfileAsync(profile As UserProfile, Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)
        If profile Is Nothing OrElse String.IsNullOrWhiteSpace(profile.UserId) Then
            Return False
        End If

        Try
            cancellationToken.ThrowIfCancellationRequested()

            Dim file = Await LocalFolder.CreateFileAsync(PROFILE_FILE, CreationCollisionOption.ReplaceExisting)
            Dim json = SerializeToJson(profile)
            Await FileIO.WriteTextAsync(file, json).AsTask(cancellationToken)

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
    ''' Loads the cached profile, or Nothing if none is cached.
    ''' </summary>
    Public Shared Async Function LoadProfileAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of UserProfile)
        Try
            cancellationToken.ThrowIfCancellationRequested()

            Dim file = Await LocalFolder.GetFileAsync(PROFILE_FILE)
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
    ''' Clears the cached profile (called on sign-out).
    ''' </summary>
    Public Shared Async Function ClearProfileAsync() As Task
        Try
            Dim file = Await LocalFolder.GetFileAsync(PROFILE_FILE)
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
