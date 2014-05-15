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
    public partial class Form1 : Form
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
        
        private string curPlayBack = "";
        private int playbackLenMillis;
        private int playbackTimeCnt;
        private int timerCnt;

        private System.Threading.Timer timer;
        private System.Threading.Timer trackbarTimer;
        
        private System.Threading.Thread trackbarThread;

        private Stopwatch stopwatch;

        // delgates to make thread safe control calls
        private delegate void SetLabelTextCallBack(Label label, string text);
        private delegate void SetTrackbarCallBack(TrackBar bar, int pos);
        private delegate void MciSendStringCallBack(string mciCommand,
                                                    StringBuilder mciRetInfo,
                                                    int infoLen,
                                                    IntPtr callBack);

        public Form1()
        {
            InitializeComponent();
            mciRetInfo = new StringBuilder(MCI_RET_INFO_BUF_LEN);
        }

        private void resetUI()
        {
            soundTrackBar.Value = 0;
            timerLabel.Text = "00:00:00";
        }

        private void Form1Load(object sender, EventArgs e)
        {
            statusLabel.Text = "Ready.";
            statusLabel.Visible = true;
            resetUI();
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

        private void ThreadSafeUpdateTrackbar(TrackBar bar, int value)
        {
            if (bar.InvokeRequired)
            {
                SetTrackbarCallBack callback = new SetTrackbarCallBack(ThreadSafeUpdateTrackbar);
                Invoke(callback, new object[] { bar, value });
            }
            else
            {
                int temp = (int) (value / (double) playbackLenMillis * bar.Maximum);
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
                MciSendStringCallBack mciCallBack = new MciSendStringCallBack(ThreadSafeMCI);
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

        private void TimerEvent(Object o)
        {
            ThreadSafeUpdateLabelText(timerLabel, ConvertMillisToTime(timerCnt * 1000));
            timerCnt++;
        }

        private void TrackbarTimerEvent(Object o)
        {
            ThreadSafeUpdateTrackbar(soundTrackBar, playbackTimeCnt * 50);
            playbackTimeCnt++;
            System.Console.WriteLine(playbackTimeCnt);
        }

        private void TrackbarEvent(Object o)
        {
            stopwatch = Stopwatch.StartNew();

            try
            {
                while (true)
                {
                    if (stopwatch.ElapsedMilliseconds % 5 == 0)
                    {
                        ThreadSafeUpdateTrackbar(soundTrackBar, (int)stopwatch.ElapsedMilliseconds);
                    }
                }
            }
            catch (ThreadInterruptedException interrupt)
            {
                ThreadSafeUpdateTrackbar(soundTrackBar, playbackLenMillis);
            }
        }

        private void RecButtonClick(object sender, EventArgs e)
        {
            // close unfinished session
            mciSendString("close sound", null, 0, IntPtr.Zero);
            
            // UI settings
            resetUI();
            statusLabel.Text = "Recording...";
            statusLabel.Visible = true;

            // start recording
            mciSendString("open new type waveaudio alias sound", null, 0, IntPtr.Zero);
            mciSendString("record sound", null, 0, IntPtr.Zero);
            
            // start the timer
            timerCnt = 0;
            timer = new System.Threading.Timer(TimerEvent, null, 0, 1000);
        }

        private void StopButtonClick(object sender, EventArgs e)
        {
            // stop timer and update status label
            statusLabel.Text = "Ready.";
            timer.Dispose();

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
            if (curPlayBack == "")
            {
                OpenFileDialog open = new OpenFileDialog();
                open.Filter = "wave|*.wav";

                if (open.ShowDialog() == DialogResult.OK)
                {
                    curPlayBack = open.FileName;
                }
            }

            // get play back length
            mciSendString("open \"" + curPlayBack + "\" alias sound", null, 0, IntPtr.Zero);
            mciSendString("status sound length", mciRetInfo, MCI_RET_INFO_BUF_LEN, IntPtr.Zero);
            playbackLenMillis = int.Parse(mciRetInfo.ToString());
            System.Console.WriteLine("total len" + playbackLenMillis);
            
            // UI settings
            resetUI();
            statusLabel.Text = "Play Back...";
            statusLabel.Visible = true;

            // start the timer and track bar
            playbackTimeCnt = 0;
            timerCnt = 0;
            //trackbarTimer = new System.Threading.Timer(TrackbarTimerEvent, null, 0, 50);
            timer = new System.Threading.Timer(TimerEvent, null, 0, 1000);
            trackbarThread = new Thread(TrackbarEvent);
            trackbarThread.Start();
            
            // start play back
            mciSendString("play sound notify", null, 0, this.Handle);
        }

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
                        timer.Dispose();
                        trackbarThread.Interrupt();
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
