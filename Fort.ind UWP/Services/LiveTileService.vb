Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Text
Imports Windows.UI.Notifications
Imports Windows.Data.Xml.Dom

''' <summary>
''' Service for managing Live Tile updates with news and notifications
''' </summary>
Public Class LiveTileService

    ''' <summary>
    ''' Updates the Live Tile with the latest news
    ''' </summary>
    Public Shared Sub UpdateTileWithNews(title As String, message As String, Optional branding As String = "name", Optional animationType As TileAnimation = TileAnimation.FadeIn)
        Try
            ' Create the tile notification content
            Dim tileXml = CreateTileXml(title, message, branding, animationType)

            ' Create and send the notification
            Dim tileNotification As New TileNotification(tileXml)
            TileUpdateManager.CreateTileUpdaterForApplication().Update(tileNotification)
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: UpdateTileWithNews failed – {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Updates the Live Tile with multiple news items that cycle
    ''' </summary>
    Public Shared Sub UpdateTileWithMultipleNews(newsItems As List(Of NewsItem))
        If newsItems Is Nothing OrElse newsItems.Count = 0 Then Return

        Try
            ' Enable notification queue to show multiple tiles
            Dim tileUpdater = TileUpdateManager.CreateTileUpdaterForApplication()
            tileUpdater.EnableNotificationQueue(True)

            ' Clear existing notifications
            tileUpdater.Clear()

            ' Animation types to cycle through
            Dim animations As TileAnimation() = {
                TileAnimation.FadeIn,
                TileAnimation.SlideUp,
                TileAnimation.SlideDown,
                TileAnimation.SlideLeft,
                TileAnimation.SlideRight
            }

            ' Add each news item (max 5 in queue)
            For i = 0 To Math.Min(newsItems.Count - 1, 4)
                Dim item = newsItems(i)
                If item Is Nothing Then Continue For

                Dim animation = animations(i Mod animations.Length)
                Dim tileXml = CreateTileXml(item.Title, item.Message, "name", animation)
                Dim tileNotification As New TileNotification(tileXml)
                tileNotification.Tag = If(String.IsNullOrWhiteSpace(item.Tag), $"news{i}", item.Tag)
                tileUpdater.Update(tileNotification)
            Next
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: UpdateTileWithMultipleNews failed – {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Creates the tile XML for different tile sizes with animations
    ''' </summary>
    Private Shared Function CreateTileXml(title As String, message As String, branding As String, Optional animation As TileAnimation = TileAnimation.FadeIn) As XmlDocument
        Dim textStyleAttr = GetTextStyleAttribute(animation)
        Dim safeBranding = EscapeXml(branding)
        Dim safeTitle = EscapeXml(title)
        Dim safeMessage = EscapeXml(message)
        Dim smallTileText = EscapeXml(GetTileMonogram(title, branding))

        ' Adaptive tile template supporting all sizes with validated content
        Dim tileXmlString = $"
<tile>
    <visual branding=""{safeBranding}"" displayName=""Fort.ind"">
        <binding template=""TileSmall"" hint-textStacking=""center"">
            <text hint-style=""caption"" hint-align=""center"">{smallTileText}</text>
        </binding>

        <!-- Medium Tile (150x150) -->
        <binding template=""TileMedium"">
            <group>
                <subgroup>
                    <text {textStyleAttr} hint-wrap=""true"">{safeTitle}</text>
                    <text hint-style=""captionSubtle"" hint-wrap=""true"" hint-maxLines=""3"">{safeMessage}</text>
                </subgroup>
            </group>
        </binding>

        <!-- Wide Tile (310x150) -->
        <binding template=""TileWide"">
            <group>
                <subgroup>
                    <text {textStyleAttr}>{safeTitle}</text>
                    <text hint-style=""body"" hint-wrap=""true"" hint-maxLines=""2"">{safeMessage}</text>
                </subgroup>
            </group>
        </binding>

        <!-- Large Tile (310x310) -->
        <binding template=""TileLarge"" hint-textStacking=""center"">
            <group>
                <subgroup>
                    <text {textStyleAttr} hint-align=""center"">{safeTitle}</text>
                </subgroup>
            </group>
            <text hint-style=""body"" hint-wrap=""true"" hint-maxLines=""6"" hint-align=""center"">{safeMessage}</text>
            <text hint-style=""captionSubtle"" hint-align=""center"">Fort.ind Desktop</text>
        </binding>

    </visual>
</tile>"

        Dim tileXml As New XmlDocument()
        tileXml.LoadXml(tileXmlString)
        Return tileXml
    End Function

    ''' <summary>
    ''' Gets the animation attribute string for the tile
    ''' </summary>
    Private Shared Function GetTextStyleAttribute(animation As TileAnimation) As String
        Select Case animation
            Case TileAnimation.FadeIn
                Return "hint-style=""captionSubtle"""
            Case TileAnimation.SlideUp
                Return "hint-style=""base"""
            Case TileAnimation.SlideDown
                Return "hint-style=""body"""
            Case TileAnimation.SlideLeft
                Return "hint-style=""bodySubtle"""
            Case TileAnimation.SlideRight
                Return "hint-style=""subtitle"""
            Case Else
                Return ""
        End Select
    End Function

    ''' <summary>
    ''' Escapes special XML characters
    ''' </summary>
    Private Shared Function EscapeXml(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ""

        Dim sanitized As New StringBuilder(text.Length)
        For Each ch As Char In text
            If (ch = vbTab) OrElse (ch = vbLf) OrElse (ch = vbCr) OrElse
               (ch >= ChrW(&H20) AndAlso ch <= ChrW(&HD7FF)) OrElse
               (ch >= ChrW(&HE000) AndAlso ch <= ChrW(&HFFFD)) Then
                sanitized.Append(ch)
            End If
        Next

        Return sanitized.ToString().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("""", "&quot;").Replace("'", "&apos;")
    End Function

    ''' <summary>
    ''' Updates the badge on the tile (shows a number or glyph)
    ''' </summary>
    Public Shared Sub UpdateBadge(count As Integer)
        Try
            If count <= 0 Then
                ClearBadge()
                Return
            End If

            Dim clampedCount = Math.Min(count, 99)
            Dim badgeXml = $"<badge value=""{clampedCount}""/>"
            Dim badgeDoc As New XmlDocument()
            badgeDoc.LoadXml(badgeXml)

            Dim badgeNotification As New BadgeNotification(badgeDoc)
            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badgeNotification)
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: UpdateBadge failed – {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Updates the badge with a glyph (icon)
    ''' </summary>
    Public Shared Sub UpdateBadgeGlyph(glyph As String)
        If String.IsNullOrWhiteSpace(glyph) Then Return

        Try
            Dim normalizedGlyph = glyph.Trim()
            If Not IsSupportedBadgeGlyph(normalizedGlyph) Then
                Debug.WriteLine($"LiveTileService: UpdateBadgeGlyph skipped unsupported glyph '{normalizedGlyph}'.")
                Return
            End If

            ' Available glyphs: none, activity, alarm, alert, attention, available, away, busy,
            ' error, newMessage, paused, playing, unavailable
            Dim badgeXml = $"<badge value=""{normalizedGlyph}""/>"
            Dim badgeDoc As New XmlDocument()
            badgeDoc.LoadXml(badgeXml)

            Dim badgeNotification As New BadgeNotification(badgeDoc)
            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badgeNotification)
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: UpdateBadgeGlyph failed – {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Shows a Windows toast notification with a title and message.
    ''' Returns False (and writes to Debug output) if notifications are blocked or an error occurs.
    ''' </summary>
    Public Shared Function SendToast(title As String, message As String) As Boolean
        Try
            Dim notifier = ToastNotificationManager.CreateToastNotifier()
            If notifier.Setting <> NotificationSetting.Enabled Then
                Debug.WriteLine($"LiveTileService: Toast suppressed – NotificationSetting is {notifier.Setting}. " &
                                "Enable notifications for this app in Windows Settings > System > Notifications.")
                Return False
            End If

            Dim toastXml As New XmlDocument()
            toastXml.LoadXml(
                $"<toast><visual><binding template=""ToastGeneric"">" &
                $"<text>{EscapeXml(title)}</text>" &
                $"<text>{EscapeXml(message)}</text>" &
                "</binding></visual></toast>")
            Dim toast As New ToastNotification(toastXml)
            notifier.Show(toast)
            Return True
        Catch ex As Exception
            Debug.WriteLine($"LiveTileService: SendToast failed – {ex.GetType().Name}: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Clears the Live Tile back to default
    ''' </summary>
    Public Shared Sub ClearTile()
        TileUpdateManager.CreateTileUpdaterForApplication().Clear()
    End Sub

    ''' <summary>
    ''' Clears the badge
    ''' </summary>
    Public Shared Sub ClearBadge()
        BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear()
    End Sub

    Private Shared Function GetTileMonogram(primaryText As String, fallbackText As String) As String
        Dim source = If(String.IsNullOrWhiteSpace(primaryText), fallbackText, primaryText)
        If String.IsNullOrWhiteSpace(source) Then Return "FI"

        Dim trimmed = source.Trim()
        If trimmed.Length <= 2 Then Return trimmed.ToUpperInvariant()
        Return trimmed.Substring(0, 2).ToUpperInvariant()
    End Function

    Private Shared Function IsSupportedBadgeGlyph(glyph As String) As Boolean
        Select Case glyph.ToLowerInvariant()
            Case "none", "activity", "alarm", "alert", "attention", "available", "away", "busy", "error", "newmessage", "paused", "playing", "unavailable"
                Return True
            Case Else
                Return False
        End Select
    End Function

End Class

''' <summary>
''' Tile animation types
''' </summary>
Public Enum TileAnimation
    FadeIn
    SlideUp
    SlideDown
    SlideLeft
    SlideRight
End Enum

''' <summary>
''' Represents a news item for the Live Tile
''' </summary>
Public Class NewsItem
    Public Property Title As String
    Public Property Message As String
    Public Property Tag As String
    Public Property Timestamp As DateTime

    Public Sub New(title As String, message As String, Optional tag As String = Nothing)
        Me.Title = title
        Me.Message = message
        Me.Tag = tag
        Me.Timestamp = DateTime.Now
    End Sub
End Class
