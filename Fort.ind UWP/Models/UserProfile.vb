Imports System.Runtime.Serialization

''' <summary>
''' A cached snapshot of the signed-in fort.social (Misskey) account.
''' The instance is the source of truth; this is a local copy for offline display.
''' </summary>
<DataContract>
Public Class UserProfile

    ''' <summary>
    ''' The Misskey user ID on the instance.
    ''' </summary>
    <DataMember>
    Public Property UserId As String

    ''' <summary>
    ''' Misskey username (without the leading @ or host).
    ''' </summary>
    <DataMember>
    Public Property Username As String

    ''' <summary>
    ''' Remote instance host, or Nothing/empty for a local fort.social account.
    ''' </summary>
    <DataMember>
    Public Property Host As String

    ''' <summary>
    ''' Display name shown in the app
    ''' </summary>
    <DataMember>
    Public Property DisplayName As String

    ''' <summary>
    ''' Bio/description, as set on fort.social
    ''' </summary>
    <DataMember>
    Public Property Bio As String

    ''' <summary>
    ''' URL of the user's avatar image on the instance.
    ''' </summary>
    <DataMember>
    Public Property AvatarUrl As String

    ''' <summary>
    ''' When the fort.social account was created
    ''' </summary>
    <DataMember>
    Public Property CreatedDate As DateTime

    ''' <summary>
    ''' Last time this app signed the user in
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

    Public Sub New()
        Preferences = New UserPreferences()
    End Sub

    ''' <summary>
    ''' Returns a deep copy of this profile. Used so edits can be saved on a detached copy
    ''' and only swapped into the shared CurrentUser after the save succeeds, avoiding
    ''' readers observing a half-applied mutation.
    ''' </summary>
    Public Function Clone() As UserProfile
        Dim copy As New UserProfile() With {
            .UserId = Me.UserId,
            .Username = Me.Username,
            .Host = Me.Host,
            .DisplayName = Me.DisplayName,
            .Bio = Me.Bio,
            .AvatarUrl = Me.AvatarUrl,
            .CreatedDate = Me.CreatedDate,
            .LastLoginDate = Me.LastLoginDate
        }
        Dim prefs = Me.Preferences
        copy.Preferences = New UserPreferences() With {
            .EnableLiveTile = prefs.EnableLiveTile,
            .EnableNotifications = prefs.EnableNotifications,
            .Theme = prefs.Theme
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

End Class
