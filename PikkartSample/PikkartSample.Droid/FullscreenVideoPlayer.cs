using System;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Media;
using Java.Util.Concurrent.Locks;
using Android.Content.PM;
using Android.Content.Res;
using Java.IO;
using Android.Util;
using Android.Graphics;

namespace PikkartSample.Droid
{

    /**
     * \class FullscreenVideoPlayer
     * \brief A fullscreen video player activity
     *
     * The AR Video player class.
     * It encapsulates a MediaPlayer and a SurfaceTexture on which the video data is stored for
     * later use by out OpenGL renderer. The class also manages the video state.
     */
    public class FullscreenVideoPlayer : Activity, MediaPlayer.IOnPreparedListener,
        MediaPlayer.IOnCompletionListener, MediaPlayer.IOnErrorListener, MediaPlayer.IOnBufferingUpdateListener,
        ISurfaceHolderCallback, MediaController.IMediaPlayerControl
    {
        private FrameLayout mRootLayout = null; /**< the root of the activity layout */
        private VideoView mVideoView = null; /**< the video view widget */
        private ISurfaceHolder mHolder = null; /**< a surface holder pointer */
        private MediaPlayer mMediaPlayer = null; /**< the video mediaplayer */
        private MediaController mMediaController = null; /**< the video mediacontroller */

        private string mMovieUrl = ""; /**< the movie file (URL or file path)*/
        private int mSeekPosition = 0; /**< where to start video playback*/
        private int mRequestedOrientation = 0; /**< video starting orientation */
        private bool mAutostart = false; /**< where to start video playback*/
        private int mCurrentBufferingPercent = 0; /**< buffering percentage*/

        private ReentrantLock mMediaPlayerLock = null; /**< media player mutex */
        private ReentrantLock mMediaControllerLock = null; /**< surfacetexture mutex*/

        /**
         * \brief The activity onCreate function
         * @param savedInstanceState
         */
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            //programmatically create the layout
            mRootLayout = new FrameLayout(this);
            FrameLayout.LayoutParams rootlp = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent);
            mVideoView = new VideoView(this);
            FrameLayout.LayoutParams videolp = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent,
                    ViewGroup.LayoutParams.WrapContent);
            mVideoView.LayoutParameters = videolp;
            mRootLayout.AddView(mVideoView);
            SetContentView(mRootLayout);
            //create the locks
            mMediaControllerLock = new ReentrantLock();
            mMediaPlayerLock = new ReentrantLock();
            //get all passed params
            mSeekPosition = Intent.GetIntExtra("seekposition", 0);
            mMovieUrl = Intent.GetStringExtra("movieurl");
            mRequestedOrientation = Intent.GetIntExtra("requestedorientation", 0);
            mAutostart = Intent.GetBooleanExtra("autostart", false);

            RequestedOrientation = (ScreenOrientation)mRequestedOrientation;

            mHolder = mVideoView.Holder;
            mHolder.AddCallback(this);

            mVideoView.Click += delegate (object sender, EventArgs e)
            {
                mMediaControllerLock.Lock();
                // This simply toggles the MediaController visibility:
                if (mMediaController != null)
                {
                    if (mMediaController.IsShowing)
                    {
                        mMediaController.Hide();
                    }
                    else
                    {
                        mMediaController.Show();
                    }
                }
                mMediaControllerLock.Unlock();
            };
        }


        /**
         * \brief Create the media player and load video
         */
        private void createMediaPlayer()
        {
            mMediaPlayerLock.Lock();
            mMediaControllerLock.Lock();

            mMediaPlayer = new MediaPlayer();
            mMediaController = new MediaController(this);

            AssetFileDescriptor afd = null;
            bool fileExist = true;
            try
            {
                afd = Assets.OpenFd(mMovieUrl);
            }
            catch (IOException e)
            {
                fileExist = false;
            }
            if (afd == null)
            {
                fileExist = false;
            }
            try
            {
                if (fileExist)
                {
                    mMediaPlayer.SetDataSource(afd.FileDescriptor, afd.StartOffset, afd.Length);
                    afd.Close();
                }
                else
                {
                    string URL_REGEX = "^((https?|ftp)://|(www|ftp)\\.)[a-z0-9-]+(\\.[a-z0-9-]+)+((:[0-9]+)|)+([/?].*)?$"; //should be ok
                    Java.Util.Regex.Pattern p = Java.Util.Regex.Pattern.Compile(URL_REGEX);
                    Java.Util.Regex.Matcher m = p.Matcher(mMovieUrl);//replace with string to compare
                    if (m.Find())
                    {
                        mMediaPlayer.SetDataSource(mMovieUrl);
                    }
                }

                mMediaPlayer.SetDisplay(mHolder);
                mMediaPlayer.PrepareAsync();
                mMediaPlayer.SetOnPreparedListener(this);
                mMediaPlayer.SetOnErrorListener(this);
                mMediaPlayer.SetOnCompletionListener(this);
                mMediaPlayer.SetAudioStreamType(Stream.Music);
            }
            catch (Exception e)
            {
                Log.Error("PikkartFullscreenVideo", "error while creating the MediaPlayer: " + e.ToString());
                prepareForTermination();
                destroyMediaPlayer();
                Finish();
            }

            mMediaControllerLock.Unlock();
            mMediaPlayerLock.Unlock();
        }


        /**
         * \brief destroy the media player and the media controller
         */
        private void destroyMediaPlayer()
        {
            mMediaControllerLock.Lock();
            if (mMediaController != null)
            {
                mMediaController.RemoveAllViews();
                mMediaController = null;
            }
            mMediaControllerLock.Unlock();

            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                try
                {
                    mMediaPlayer.Stop();
                }
                catch (Exception e)
                {
                    mMediaPlayerLock.Unlock();
                    Log.Error("PikkartFullscreenVideo", "could not stop playback");
                }
                mMediaPlayer.Release();
                mMediaPlayer = null;
            }
            mMediaPlayerLock.Unlock();
        }

        /**
         * \brief destroy al views and force garbage collection
         */
        private void destroyView()
        {
            mVideoView = null;
            mHolder = null;
            Java.Lang.JavaSystem.Gc();
        }

        /**
         * \brief The activity onDestroy function
         */
        protected override void OnDestroy()
        {
            prepareForTermination();
            base.OnDestroy();
            destroyMediaPlayer();
            mMediaPlayerLock = null;
            mMediaControllerLock = null;
        }

        /**
         * \brief The activity onResume function
         */
        protected override void OnResume()
        {
            base.OnResume();
            RequestedOrientation = (ScreenOrientation)mRequestedOrientation;
            mHolder = mVideoView.Holder;
            mHolder.AddCallback(this);
        }

        /**
         * \brief The activity onConfigurationChanged function
         */
        public override void OnConfigurationChanged(Configuration newConfig)
        {
            base.OnConfigurationChanged(newConfig);
        }

        /**
         * \brief prepare objects for termination (stop mediaplayers, unload video, etc.)
         */
        private void prepareForTermination()
        {
            mMediaControllerLock.Lock();
            if (mMediaController != null)
            {
                mMediaController.Hide();
                mMediaController.RemoveAllViews();
            }
            mMediaControllerLock.Unlock();

            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                mSeekPosition = mMediaPlayer.CurrentPosition;
                bool wasPlaying = mMediaPlayer.IsPlaying;
                if (wasPlaying)
                {
                    try
                    {
                        mMediaPlayer.Pause();
                    }
                    catch (Exception e)
                    {
                        mMediaPlayerLock.Unlock();
                        Log.Error("PikkartFullscreenVideo", "could not pause playback");
                    }
                }
            }
            mMediaPlayerLock.Unlock();
        }

        /**
         * \brief on back button pressure funtion
         */
        public override void OnBackPressed()
        {
            prepareForTermination();
            base.OnBackPressed();
        }

        /**
         * \brief The activity onPause function
         */
        protected override void OnPause()
        {
            base.OnPause();
            prepareForTermination();
            destroyMediaPlayer();
            destroyView();
        }

        #region ISurfaceHolderCallback
        /**
         * \brief Callback for interface SurfaceHolder.Callback, surface holder created
         * @param holder the created SurfaceHolder
         */
        public void SurfaceCreated(ISurfaceHolder holder)
        {
            createMediaPlayer();
        }

        /**
         * \brief Callback for interface SurfaceHolder.Callback, surface holder modified
         * @param holder the modified SurfaceHolder
         * @param format new data format
         * @param width new width
         * @param height new height
         */
        public void SurfaceChanged(ISurfaceHolder holder, [GeneratedEnum] Format format, int width, int height)
        {
        }

        /**
         * \brief Callback for interface SurfaceHolder.Callback, surface holder destroyed
         * @param holder the created SurfaceHolder
         */
        public void SurfaceDestroyed(ISurfaceHolder holder)
        {
        }
        #endregion


        /**
         * \brief Callback for interface MediaPlayer.OnPreparedListener
         * @param mediaPlayer
         */
        public void OnPrepared(MediaPlayer mp)
        {
            mMediaControllerLock.Lock();
            mMediaPlayerLock.Lock();
            if ((mMediaController != null) && (mVideoView != null) && (mMediaPlayer != null))
            {
                if (mVideoView.Parent != null)
                {
                    mMediaController.SetMediaPlayer(this);
                    View anchorView = mVideoView.Parent is View ? (View)mVideoView.Parent : mVideoView;
                    mMediaController.SetAnchorView(anchorView);
                    mVideoView.SetMediaController(mMediaController);
                    mMediaController.Enabled = true;
                    try
                    {
                        mMediaPlayer.SeekTo(mSeekPosition);
                    }
                    catch (Exception e)
                    {
                        mMediaPlayerLock.Unlock();
                        mMediaControllerLock.Unlock();
                        Log.Error("PikkartFullscreenVideo", "Could not seek to a position");
                    }
                    if (mAutostart)
                    {
                        try
                        {
                            mMediaPlayer.Start();
                            //mAutostart = false;
                        }
                        catch (Exception e)
                        {
                            mMediaPlayerLock.Unlock();
                            mMediaControllerLock.Unlock();
                            Log.Error("PikkartFullscreenVideo", "Could not start playback");
                        }
                    }
                    mMediaController.Show();
                }
            }
            mMediaPlayerLock.Unlock();
            mMediaControllerLock.Unlock();
        }

        /**
         * \brief Callback for interface MediaPlayer.OnCompletionListener
         * @param mediaPlayer
         */
        public void OnCompletion(MediaPlayer mp)
        {
            prepareForTermination();
            Finish();
        }

        /**
         * \brief Callback for interface MediaPlayer.OnErrorListener
         * @param mediaPlayer
         * @param i error code
         * @param i1 error sub-code
         * @return true if the error is relative to this video player media player
         */
        public bool OnError(MediaPlayer mp, [GeneratedEnum] MediaError what, int extra)
        {
            if (mp == mMediaPlayer)
            {
                string errorDescription;
                switch (what)
                {
                    case MediaError.NotValidForProgressivePlayback:
                        errorDescription = "The video is streamed and its container is not valid for progressive playback";
                        break;
                    case MediaError.ServerDied:
                        errorDescription = "Media server died";
                        break;
                    case MediaError.Unknown:
                        errorDescription = "Unspecified media player error";
                        break;
                    default:
                        errorDescription = "Unknown error " + what;
                        break;
                }
                Log.Error("PikkartFullscreenVideo", "Error while opening the file for fullscreen. Unloading the media player (" + errorDescription + ", " + extra + ")");
                prepareForTermination();
                destroyMediaPlayer();
                Finish();
                return true;
            }

            return false;
        }

        /**
         * \brief Callback for interface MediaPlayer.OnBufferingUpdateListener
         * @param mediaPlayer
         * @param i the buffering percentage (int from 0 to 100)
         */
        public void OnBufferingUpdate(MediaPlayer mp, int percent)
        {
            mCurrentBufferingPercent = percent;
        }

        public int AudioSessionId => 0;

        public int BufferPercentage => mCurrentBufferingPercent;

        public int CurrentPosition => getCalcCurrentPosition();
        private int getCalcCurrentPosition()
        {
            int result = 0;
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
                result = mMediaPlayer.CurrentPosition;
            mMediaPlayerLock.Unlock();
            return result;
        }

        public int Duration => getCalcDuration();
        private int getCalcDuration()
        {
            int result = 0;
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
                result = mMediaPlayer.Duration;
            mMediaPlayerLock.Unlock();
            return result;
        }

        public bool IsPlaying => isCalcPlaying();
        private bool isCalcPlaying()
        {
            bool result = false;
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
                result = mMediaPlayer.IsPlaying;
            mMediaPlayerLock.Unlock();
            return result;
        }

        public bool CanPause()
        {
            return true;
        }

        public bool CanSeekBackward()
        {
            return true;
        }

        public bool CanSeekForward()
        {
            return true;
        }

        public void Pause()
        {
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                try
                {
                    mMediaPlayer.Pause();
                }
                catch (Exception e)
                {
                    mMediaPlayerLock.Unlock();
                }
            }
            mMediaPlayerLock.Unlock();
        }

        public void SeekTo(int pos)
        {
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                try
                {
                    mMediaPlayer.SeekTo(pos);
                }
                catch (Exception e)
                {
                    mMediaPlayerLock.Unlock();
                }
            }
            mMediaPlayerLock.Unlock();
        }

        public void Start()
        {
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                try
                {
                    mMediaPlayer.Start();
                }
                catch (Exception e)
                {
                    mMediaPlayerLock.Unlock();
                }
            }
            mMediaPlayerLock.Unlock();
        }
    }
}