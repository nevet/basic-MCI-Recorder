using System;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace MCIRecorder
{
    public partial class RecorderUI : Form
    {
        [DllImport("winmm.dll")]
        private static extern int mciSendString(string mciCommand,
                                                StringBuilder mciRetInfo,
                                                int infoLen,
                                                IntPtr callBack);
        
        private const int MM_MCINOTIFY = 0x03B9;
        private const int MCI_NOTIFY_SUCCESS = 0x01;
        private const int MCI_NOTIFY_SUPERSEDED = 0x02;
        private const int MCI_NOTIFY_ABORTED = 0x04;
        private const int MCI_NOTIFY_FAILURE = 0x08;

        private const int MCI_RET_INFO_BUF_LEN = 128;

        private StringBuilder mciRetInfo;
        
        private string _curPlayBack = "";
        private int _playbackLenMillis;
        private int _playbackTimeCnt;
        private int _timerCnt;

        private System.Threading.Timer _timer;
        private Thread _trackbarThread;

        private Stopwatch _stopwatch;

        // delgates to make thread safe control calls
        private delegate void SetLabelTextCallBack(Label label, string text);
        private delegate void SetTrackbarCallBack(TrackBar bar, int pos);
        private delegate void MCISendStringCallBack(string mciCommand,
                                                    StringBuilder mciRetInfo,
                                                    int infoLen,
                                                    IntPtr callBack);

        public RecorderUI()
        {
            InitializeComponent();
            mciRetInfo = new StringBuilder(MCI_RET_INFO_BUF_LEN);
        }

        private void RecorderUILoad(object sender, EventArgs e)
        {
            statusLabel.Text = "Ready.";
            statusLabel.Visible = true;
            ResetUI();
        }

        # region Helper Functions
        private void ResetUI()
        {
            soundTrackBar.Value = 0;
            timerLabel.Text = "00:00:00";
        }

        /// <summary>
        /// This function will convert a time in milli-second to HH:MM:SS:MMS
        /// </summary>
        /// <param name="millis">Time in millis.</param>
        /// <returns>A string in HH:MM:SS:MMS format.</returns>
        private string ConvertMillisToTime(long millis)
        {
            int ms, s, m, h;

            ms = (int)millis % 1000;
            millis /= 1000;

            s = (int)(millis % 60);
            millis /= 60;

            m = (int)(millis % 60);
            millis /= 60;

            h = (int)(millis % 60);
            millis /= 60;

            return System.String.Format("{0:D2}:{1:D2}:{2:D2}", h, m, s);
        }

        private string ConvertMillisToTime(int millis)
        {
            return ConvertMillisToTime((long) millis);
        }
        # endregion

        # region Thread Safe Control Methods
        private void ThreadSafeUpdateLabelText(Label label, string time)
        {
            if (label.InvokeRequired)
            {
                SetLabelTextCallBack callback = new SetLabelTextCallBack(ThreadSafeUpdateLabelText);
                Invoke(callback, new object[] { label, time });
            }
            else
            {
                label.Text = time;
            }
        }

        private void ThreadSafeUpdateTrackbarValue(TrackBar bar, int value)
        {
            if (bar.InvokeRequired)
            {
                SetTrackbarCallBack callback = new SetTrackbarCallBack(ThreadSafeUpdateTrackbarValue);
                Invoke(callback, new object[] { bar, value });
            }
            else
            {
                int temp = (int) (value / (double) _playbackLenMillis * bar.Maximum);
                if (temp > bar.Maximum) temp = bar.Maximum;

                bar.Value = temp;
            }
        }

        private void ThreadSafeMCI(string mciCommand,
                                   StringBuilder mciRetInfo,
                                   int infoLen,
                                   IntPtr callBack)
        {
            if (this.InvokeRequired)
            {
                MCISendStringCallBack mciCallBack = new MCISendStringCallBack(ThreadSafeMCI);
                Invoke(mciCallBack, new object[]
                                        {
                                            mciCommand,
                                            mciRetInfo,
                                            infoLen,
                                            callBack
                                        });
            }
            else
            {
                mciSendString(mciCommand,
                              mciRetInfo,
                              infoLen,
                              callBack);
            }
        }
        # endregion

        # region Timer and Trackbar Regualr Event Handlers
        private void TimerEvent(Object o)
        {
            ThreadSafeUpdateLabelText(timerLabel, ConvertMillisToTime(_timerCnt * 1000));
            _timerCnt++;
        }

        private void TrackbarEvent(Object o)
        {
            _stopwatch = Stopwatch.StartNew();

            try
            {
                while (true)
                {
                    if (_stopwatch.ElapsedMilliseconds % 5 == 0)
                    {
                        ThreadSafeUpdateTrackbarValue(soundTrackBar, (int)_stopwatch.ElapsedMilliseconds);
                    }
                }
            }
            catch (ThreadInterruptedException interrupt)
            {
                ThreadSafeUpdateTrackbarValue(soundTrackBar, _playbackLenMillis);
            }
        }
        # endregion

        # region UI Control Events
        private void RecButtonClick(object sender, EventArgs e)
        {
            // close unfinished session
            mciSendString("close sound", null, 0, IntPtr.Zero);
            
            // UI settings
            ResetUI();
            statusLabel.Text = "Recording...";
            statusLabel.Visible = true;

            // start recording
            mciSendString("open new type waveaudio alias sound", null, 0, IntPtr.Zero);
            mciSendString("record sound", null, 0, IntPtr.Zero);
            
            // start the timer
            _timerCnt = 0;
            _timer = new System.Threading.Timer(TimerEvent, null, 0, 1000);
        }

        private void StopButtonClick(object sender, EventArgs e)
        {
            // stop timer and update status label
            statusLabel.Text = "Ready.";
            _timer.Dispose();

            // stop recording
            mciSendString("stop sound", null, 0, IntPtr.Zero);
            mciSendString("status sound length", mciRetInfo, MCI_RET_INFO_BUF_LEN, IntPtr.Zero);
            // adjust the stop time difference between timer-stop and recording-stop
            timerLabel.Text = ConvertMillisToTime(int.Parse(mciRetInfo.ToString()));

            // save the recording
            SaveFileDialog save = new SaveFileDialog();
            save.Filter = "wave|*.wav";

            if (save.ShowDialog() == DialogResult.OK)
            {
                mciSendString("save sound " + save.FileName, null, 0, IntPtr.Zero);
                mciSendString("close sound", null, 0, IntPtr.Zero);
            }
            else
            {
                MessageBox.Show("Cannot save the record.");
            }
        }

        private void PlayButtonClick(object sender, EventArgs e)
        {
            // close unfinished session
            mciSendString("close sound", null, 0, IntPtr.Zero);

            // if no sound has been loaded yet, load the sound
            if (_curPlayBack == "")
            {
                OpenFileDialog open = new OpenFileDialog();
                open.Filter = "wave|*.wav";

                if (open.ShowDialog() == DialogResult.OK)
                {
                    _curPlayBack = open.FileName;
                }
            }

            // get play back length
            mciSendString("open \"" + _curPlayBack + "\" alias sound", null, 0, IntPtr.Zero);
            mciSendString("status sound length", mciRetInfo, MCI_RET_INFO_BUF_LEN, IntPtr.Zero);
            _playbackLenMillis = int.Parse(mciRetInfo.ToString());
            //System.Console.WriteLine("total len" + _playbackLenMillis);
            
            // UI settings
            ResetUI();
            statusLabel.Text = "Play Back...";
            statusLabel.Visible = true;

            // start the timer and track bar
            _playbackTimeCnt = 0;
            _timerCnt = 0;
            _timer = new System.Threading.Timer(TimerEvent, null, 0, 1000);
            _trackbarThread = new Thread(TrackbarEvent);
            _trackbarThread.Start();
            
            // start play back
            mciSendString("play sound notify", null, 0, this.Handle);
        }
        # endregion

        /// <summary>
        /// Overridden Win Form call back function, used to sniff call back
        /// messages triggered by MCI.
        /// </summary>
        /// <param name="m">A reference to the message sent by MCI.</param>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == MM_MCINOTIFY)
            {
                switch (m.WParam.ToInt32())
                {
                    case MCI_NOTIFY_SUCCESS:
                        // UI settings
                        statusLabel.Text = "Ready.";
                        playButton.Text = "Play Aagin";
                        
                        // dispose timer and track bar timers
                        _timer.Dispose();
                        _trackbarThread.Interrupt();
                        soundTrackBar.Value = soundTrackBar.Maximum;

                        break;
                    case MCI_NOTIFY_ABORTED:
                        MessageBox.Show("aborted");
                        break;
                    default:
                        MessageBox.Show("other error");
                        break;
                }
            }
            
            base.WndProc(ref m);
        }
    }
}
