Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Runtime.Remoting
Imports System.Windows.Threading
Imports Un4seen.Bass
Imports Un4seen.Bass.Misc

Class MainWindow

    Private Property RecordedData As Byte()
    Private StereoOn As Boolean = True   'hier je nach Aufnahmegerät konfigurieren
    Private MaximaleAufnahmeLaenge As Integer
    Private MeineRecordingCallback As RECORDPROC
    Private RecordingChannel As Integer
    Private PlaybackChannel As Integer
    Public WF As New WaveForm
    Private BytesWritten As Integer = 0
    Private HasRecorded As Boolean = False
    Private IsRecording As Boolean = False
    Private IsPlaying As Boolean = False
    Private ReadOnly Property MaxTrackLaengeInBytes As Long
        Get
            Return GetMaxTrackLength(CLng(Math.Floor(5 * 1024 * 1024)))
        End Get
    End Property

    Private ReadOnly Property Holeskalierung As Single
        Get
            Return 1    'hier statt der Eins Windows-Skalierungsfaktor eintragen
        End Get
    End Property

    Private _Zoom As Integer
    Public Property Zoom As Integer
        Get
            Return _Zoom
        End Get
        Set(value As Integer)
            _Zoom = value
            UpdateWaveForm(Nothing, Nothing)
        End Set
    End Property
    Private ReadOnly Property NumberOfChannels As Integer
        Get
            If StereoOn Then
                Return 2
            Else
                Return 1
            End If
        End Get
    End Property

    Private ReadOnly Property SampleFormat As BASSFlag
        Get
            If Not StereoOn Then
                Return BASSFlag.BASS_SAMPLE_8BITS
            Else
                Return BASSFlag.BASS_DEFAULT
            End If
        End Get
    End Property

    Private _RecordingXPosWF As Double
    Public Property RecordingXPosWF As Double
        Get
            Return _RecordingXPosWF
        End Get
        Set(value As Double)
            _RecordingXPosWF = value
        End Set
    End Property

    Private _ClickedXPosWF As Double
    Public Property ClickedXPosWF As Double
        Get
            Return _ClickedXPosWF
        End Get
        Set(value As Double)
            If value <= RecordingXPosWF Then
                _ClickedXPosWF = value
                'MarkerPosition = TimeSpan.FromSeconds(GetPositionInSeconds(value))
            Else
                MessageBox.Show("Du versuchst den Marker auf einen ungültigen Wert festzulegen. Bitte stelle sicher, dass du die Markerposition links von oder direkt auf dem Aufnahmefortschritt platzierst.")
            End If
        End Set
    End Property

    Private _PlayXPosWF As Double
    Public Property PlayXPosWF As Double
        Get
            Return _PlayXPosWF
        End Get
        Set(value As Double)
            If value <= RecordingXPosWF Then
                _PlayXPosWF = value
                'PlayBackPosition = TimeSpan.FromSeconds(GetPositionInSeconds(_PlayXPosWF))
            Else
                If IsPlaying Then
                    Bass.BASS_ChannelStop(PlayBackChannel)
                Else
                    MessageBox.Show("Du versuchst die Abspielposition auf einen ungültigen Wert festzulegen. Bitte stelle sicher, dass du die Abspielposition links von oder direkt auf dem Aufnahmefortschritt platzierst.")
                End If
            End If
        End Set
    End Property

    Private Function GetMaxTrackLength(argvalue As Long) As Long
        Dim Rest As Integer
        If StereoOn Then
            Rest = CInt(argvalue) Mod 2
        Else
            Rest = CInt(argvalue) Mod 1
        End If
        Return argvalue - Rest
    End Function

    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs)
        Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT, Nothing)
        Zoom = 1
        lblZoom.Content = "Zoom: 1x"
    End Sub

    Private Sub btnStarteRecording_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        If Not IsRecording Then
            MaximaleAufnahmeLaenge = CInt(MaxTrackLaengeInBytes)
            Dim Rest As Integer = MaximaleAufnahmeLaenge Mod 4
            RecordedData = New Byte(MaximaleAufnahmeLaenge - 1 - Rest) {}
            Dim Laenge As Single

            Bass.BASS_RecordInit(-1)   'hier ggf anderes Aufnahmegerät angeben

            MeineRecordingCallback = New RECORDPROC(AddressOf MyRecording)

            RecordingChannel = Bass.BASS_RecordStart(8000, NumberOfChannels, BASSFlag.BASS_RECORD_PAUSE Or SampleFormat, 500, MeineRecordingCallback, IntPtr.Zero) 'hier statt 481000 Samplinrate des Aufnahmegeräts eintragen

            Bass.BASS_ChannelPlay(RecordingChannel, False)

            'ChannelToRender = RecordingChannel
            'PeakChannel = ChannelToRender
            'If Not PeakTimer.Enabled Then PeakTimer.Start()
            InitialisiereWaveForm()
            Laenge = CSng(Bass.BASS_ChannelBytes2Seconds(RecordingChannel, CLng(MaxTrackLaengeInBytes)))
            WF.RenderStartRecording(RecordingChannel, Laenge, 0)
        Else
            RecordingStopped()
        End If
        IsRecording = Not IsRecording
    End Sub

    Private Function MyRecording(handle As Integer, buffer As IntPtr, length As Integer, user As IntPtr) As Boolean
        Dim cont As Boolean = True

        If length > 0 AndAlso buffer <> IntPtr.Zero Then

            Dim PufferGroesse As Integer

            If BytesWritten + length > RecordedData.Length Then
                PufferGroesse = RecordedData.Length - BytesWritten
            Else
                PufferGroesse = length
            End If

            Marshal.Copy(buffer, RecordedData, BytesWritten, PufferGroesse)
            BytesWritten += PufferGroesse

            Dim Prozent As Double = BytesWritten / MaxTrackLaengeInBytes * 100
            Me.Dispatcher.Invoke(Sub() RecordingXPosWF = imgWF.Width / 100 * Prozent)
            'RecordingPosition = TimeSpan.FromSeconds(GetPositionInSeconds(RecordingXPosWF))

            Me.Dispatcher.InvokeAsync(Sub() WF.RenderRecording(buffer, PufferGroesse), DispatcherPriority.SystemIdle)
            Me.Dispatcher.InvokeAsync(Sub() UpdateWaveForm(Nothing, Nothing), DispatcherPriority.SystemIdle)

            If BytesWritten >= MaxTrackLaengeInBytes Then

                cont = False ' stop recording
                Me.Dispatcher.InvokeAsync(Sub() RecordingStopped(), DispatcherPriority.SystemIdle)

                MessageBox.Show("Die maximale Aufnahmelänge ist erreicht worden. Die Aufnahme wurde beendet.")
            End If
        End If
        Return cont
    End Function

    Public Sub InitialisiereWaveForm()
        WF.ColorBackground = System.Drawing.Color.Black


        WF.ColorLeft = System.Drawing.Color.FromName("White")

        WF.ColorRight = System.Drawing.Color.FromName("White")
        WF.DrawEnvelope = False

        WF.DrawGradient = False
        WF.DrawWaveForm = WF.WAVEFORMDRAWTYPE.Mono
        WF.DetectBeats = True
        WF.DrawBeat = WF.BEATDRAWTYPE.Middle
        WF.BeatWidth = 2

        WF.BeatLength = 1
        WF.MarkerLength = 1
        WF.DrawMarker = WF.MARKERDRAWTYPE.Name Or WF.MARKERDRAWTYPE.Line Or WF.MARKERDRAWTYPE.NamePositionTop Or WF.MARKERDRAWTYPE.NamePositionBottom Or WF.MARKERDRAWTYPE.NamePositionMiddle Or WF.MARKERDRAWTYPE.NameBoxFilled Or WF.MARKERDRAWTYPE.NamePositionAlternate
        Dim SchriftArt As New Font("Segoe UI", 8)
        WF.MarkerFont = SchriftArt

    End Sub
    Private Sub RecordingStopped()

        Bass.BASS_ChannelStop(RecordingChannel)

        Dim Prozent As Double = BytesWritten / MaxTrackLaengeInBytes * 100
        RecordingXPosWF = imgWF.Width / 100 * Prozent

        'WF.RenderRecording(RenderBuffer, RenderLength)
        'UpdateWaveForm(Nothing, Nothing)

        'If Not IsPlaying Then PeakTimer.Stop()
        Me.Dispatcher.Invoke(Sub() btnStarteRecording.Content = "Aufnahme wurde beendet.")
        HasRecorded = True
        IsRecording = False
    End Sub
    Public Sub UpdateWaveForm(sender As Object, e As EventArgs)
        Try
            Me.Dispatcher.Invoke(Sub() WellenFormZeichnen())
        Catch ex As Exception

        End Try
    End Sub

    Public Sub WellenFormZeichnen()
        If WF IsNot Nothing Then
            If RecordingXPosWF <> 0 Then

                Dim WellenForm As Bitmap = WF.CreateBitmap(CInt(TransformToPixels(imgWF.Width)) * Zoom, CInt(TransformToPixels(imgWF.Height) * Holeskalierung), -1, -1, False)
                Dim Grafik As Graphics = Graphics.FromImage(WellenForm)
                Dim Stift As System.Drawing.Pen

                If (RecordingXPosWF <> Nothing) OrElse (RecordingXPosWF <> 0) Then

                    Stift = System.Drawing.Pens.Red
                    Grafik.DrawLine(Stift, CSng(TransformToPixels(RecordingXPosWF) * Zoom), 0, CSng(TransformToPixels(RecordingXPosWF) * Zoom), TransformToPixels(imgWF.Height))
                End If

                If (ClickedXPosWF <> Nothing) OrElse (ClickedXPosWF <> 0) Then

                    Stift = System.Drawing.Pens.Yellow
                    Grafik.DrawLine(Stift, CSng(ClickedXPosWF * Zoom - Canvas.GetLeft(imgWF)), 0, CSng(ClickedXPosWF * Zoom - Canvas.GetLeft(imgWF)), TransformToPixels(imgWF.Height))
                End If

                If (PlayXPosWF <> Nothing) OrElse (PlayXPosWF <> 0) Then

                    Stift = System.Drawing.Pens.Green
                    Grafik.DrawLine(Stift, CSng(PlayXPosWF * Zoom - Canvas.GetLeft(imgWF)), 0, CSng(PlayXPosWF * Zoom - Canvas.GetLeft(imgWF)), TransformToPixels(imgWF.Height))
                End If
                imgWF.Source = BitmapToImageSource(WellenForm)
                'RecordingPosition = TimeSpan.FromSeconds(GetPositionInSeconds(RecordingXPosWF))
            Else
                imgwf.source = bitmaptoimagesource(wf.createbitmap(CInt(transformtopixels(imgwf.width)) * zoom, CInt(transformtopixels(imgwf.height) * holeskalierung), -1, -1, False))
            End If
        Else
            imgWF.Source = Nothing
        End If
    End Sub

    Public Function TransformToPixels(Units As Double) As Integer
        Using g As Graphics = Graphics.FromHwnd(IntPtr.Zero)
            Dim retValue As Integer = CInt(((g.DpiX / 96) * Units))
            Return CInt(retValue - (retValue * (Holeskalierung - 1)))
        End Using
    End Function

    Public Function BitmapToImageSource(ByVal bitmap As Bitmap) As BitmapImage

        If bitmap Is Nothing Then Return Nothing
        Using memory As MemoryStream = New MemoryStream()
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp)
            memory.Position = 0
            Dim bitmapimage As BitmapImage = New BitmapImage()
            bitmapimage.BeginInit()
            bitmapimage.StreamSource = memory
            bitmapimage.CacheOption = BitmapCacheOption.OnLoad
            bitmapimage.EndInit()
            Return bitmapimage
        End Using
    End Function

    Private Sub btnZoomPlus_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        Zoom += 1
        lblZoom.Content = "Zoom: " & CStr(Zoom) & "x"
    End Sub

    Private Sub btnZoomMinus_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        Zoom -= 1
        lblZoom.Content = "Zoom: " & CStr(Zoom) & "x"
    End Sub

    Private Sub cnv_MouseMove(sender As Object, e As MouseEventArgs)

    End Sub

    Private Sub cnv_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        Dim MausPosition = e.GetPosition(imgWF)
        Dim abc As Double = Canvas.GetLeft(imgWF)
        ClickedXPosWF = (TransformToPixels(MausPosition.X) + TransformToPixels(Canvas.GetLeft(imgWF))) / Zoom
        UpdateWaveForm(Nothing, Nothing)
    End Sub

    Private Sub cnv_MouseRightButtonDown(sender As Object, e As MouseButtonEventArgs)
        Dim MausPosition = e.GetPosition(imgWF)
        PlayXPosWF = (TransformToPixels(MausPosition.X) + TransformToPixels(Canvas.GetLeft(imgWF))) / Zoom
        UpdateWaveForm(Nothing, Nothing)
    End Sub

    Private Sub btnRewind_Click(sender As Object, e As RoutedEventArgs)
        Canvas.SetLeft(imgWF, Canvas.GetLeft(imgWF) + 15)
    End Sub

    Private Sub btnFastForward_Click(sender As Object, e As RoutedEventArgs)
        Canvas.SetLeft(imgWF, Canvas.GetLeft(imgWF) - 15)
    End Sub
End Class
