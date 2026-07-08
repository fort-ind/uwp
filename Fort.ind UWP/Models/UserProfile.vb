Imports System.Runtime.Serialization

''' <summary>
''' Represents a user profile with account information
''' Designed for local storage now, ready for server sync later
''' </summary>
<DataContract>
Public Class UserProfile

    ''' <summary>
    ''' Unique identifier for the user (GUID for local, can map to server ID later)
    ''' </summary>
    <DataMember>
    Public Property UserId As String

    ''' <summary>
    ''' Display name shown in the app
    ''' </summary>
    <DataMember>
    Public Property DisplayName As String

    ''' <summary>
    ''' Username for login
    ''' </summary>
    <DataMember>
    Public Property Username As String

    ''' <summary>
    ''' Hashed password (for local auth, will use tokens for server)
    ''' </summary>
    <DataMember>
    Public Property PasswordHash As String

    ''' <summary>
    ''' User's email address
    ''' </summary>
    <DataMember>
    Public Property Email As String

    ''' <summary>
    ''' Optional bio/description
    ''' </summary>
    <DataMember>
    Public Property Bio As String

    ''' <summary>
    ''' Path to profile picture (local path or URL)
    ''' </summary>
    <DataMember>
    Public Property ProfilePicturePath As String

    ''' <summary>
    ''' When the account was created
    ''' </summary>
    <DataMember>
    Public Property CreatedDate As DateTime

    ''' <summary>
    ''' Last login timestamp
    ''' </summary>
    <DataMember>
    Public Property LastLoginDate As DateTime

    ''' <summary>
    ''' User preferences and settings.
    ''' Getter ensures this is never Nothing even after deserialization
    ''' (DataContractJsonSerializer bypasses the constructor).
    ''' </summary>
    <DataMember>
    Public Property Preferences As UserPreferences
        Get
            If _preferences Is Nothing Then _preferences = New UserPreferences()
            Return _preferences
        End Get
        Set(value As UserPreferences)
            _preferences = value
        End Set
    End Property
    Private _preferences As UserPreferences

    ''' <summary>
    ''' Creates a new empty profile
    ''' </summary>
    Public Sub New()
        UserId = Guid.NewGuid().ToString()
        CreatedDate = DateTime.Now
        Preferences = New UserPreferences()
    End Sub

    ''' <summary>
    ''' Creates a new profile with basic info
    ''' </summary>
    Public Sub New(username As String, displayName As String, email As String)
        Me.New()
        Me.Username = username
        Me.DisplayName = displayName
        Me.Email = email
    End Sub

    ''' <summary>
    ''' Returns a deep copy of this profile. Used so edits can be saved on a detached copy
    ''' and only swapped into the shared CurrentUser after the save succeeds, avoiding
    ''' readers observing a half-applied mutation.
    ''' </summary>
    Public Function Clone() As UserProfile
        Dim copy As New UserProfile() With {
            .UserId = Me.UserId,
            .DisplayName = Me.DisplayName,
            .Username = Me.Username,
            .PasswordHash = Me.PasswordHash,
            .Email = Me.Email,
            .Bio = Me.Bio,
            .ProfilePicturePath = Me.ProfilePicturePath,
            .CreatedDate = Me.CreatedDate,
            .LastLoginDate = Me.LastLoginDate
        }
        Dim prefs = Me.Preferences
        copy.Preferences = New UserPreferences() With {
            .EnableLiveTile = prefs.EnableLiveTile,
            .EnableNotifications = prefs.EnableNotifications,
            .Theme = prefs.Theme,
            .RememberLogin = prefs.RememberLogin
        }
        Return copy
    End Function

End Class

''' <summary>
''' User preferences and settings
''' </summary>
<DataContract>
Public Class UserPreferences

    ''' <summary>
    ''' Enable Live Tile updates
    ''' </summary>
    <DataMember>
    Public Property EnableLiveTile As Boolean = True

    ''' <summary>
    ''' Enable notifications
    ''' </summary>
    <DataMember>
    Public Property EnableNotifications As Boolean = True

    ''' <summary>
    ''' Theme preference (Dark, Light, System)
    ''' </summary>
    <DataMember>
    Public Property Theme As String = "Dark"

    ''' <summary>
    ''' Remember login between sessions
    ''' </summary>
    <DataMember>
    Public Property RememberLogin As Boolean = True

End Class
