using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Media;
using Android.Graphics;
using Java.Util.Concurrent.Locks;
using Android.Util;
using static PikkartSample.Droid.PikkartVideoPlayer.VideoSate;
using Java.IO;
using Android.Content.Res;
using Java.Util.Regex;
using Android.Content.PM;

namespace PikkartSample.Droid
{
    /**
     * \class PikkartVideoPlayer
     * \brief The AR Video player class.
     *
     * The AR Video player class.
     * It encapsulates a MediaPlayer and a SurfaceTexture on which the video data is stored for
     * later use by out OpenGL renderer. The class also manages the video state.
     */
    public class PikkartVideoPlayer : Java.Lang.Object, MediaPlayer.IOnPreparedListener, MediaPlayer.IOnBufferingUpdateListener, MediaPlayer.IOnCompletionListener,  MediaPlayer.IOnErrorListener
    {
        private Activity mParentActivity = null; /**< the parent activity */
        private MediaPlayer mMediaPlayer = null; /**< the media player that decode the media */
        private SurfaceTexture mSurfaceTexture = null; /**< the surface texture in which media(video) data is stored*/
        private string mMovieUrl = ""; /**< the movie file (URL or file path)*/
        private bool mAutoStart = true; /**< should the video autostart when ready*/
        private VIDEO_STATE mVideoState = VIDEO_STATE.NOT_READY; /**< the media player current state*/
        private Intent mPlayFullScreenIntent = null; /**< play in ullscreen intent (in case AR videos are not supported) */
        private bool mFullscreen = false; /**< should the movie play fullscreen*/
        private int mSeekPosition = -1; /**< where to start video playback*/
        private byte mTextureID = 0; /**< the opengl texture id in which to store video data*/

        private ReentrantLock mMediaPlayerLock = null; /**< media player mutex */
        private ReentrantLock mSurfaceTextureLock = null; /**< surfacetexture mutex*/

        private int mCurrentBufferingPercent = 0;

        /**< buffering percentage*/

        /**
         * \brief Get the movie file URL or file path.
         * @return the file path or URL as String.
         */
        public string getMovieUrl()
        {
            return mMovieUrl;
        }

        /**
         * \brief Get the movie status.
         * @return the movie status.
         */
        public VIDEO_STATE getVideoStatus()
        {
            return mVideoState;
        }

        /**
         * \brief Is the movie playing fullscreen.
         * @return true if fullscreen.
         */
        public bool isFullscreen()
        {
            return mFullscreen;
        }

        /**
         * \brief Get current buffering percentage
         * @return true if fullscreen.
         */
        public int getCurrentBufferingPercent()
        {
            return mCurrentBufferingPercent;
        }

        /**
         * \brief Set parent activity
         * @return true if fullscreen.
         */
        public void setActivity(Activity newActivity)
        {
            mParentActivity = newActivity;
        }


        /**
         * \brief Callback for interface MediaPlayer.OnBufferingUpdateListener
         * @param mediaPlayer
         * @param i the buffering percentage (int from 0 to 100)
         */
        public void OnBufferingUpdate(MediaPlayer mp, int percent)
        {
            mVideoState = VIDEO_STATE.BUFFERING;
            if (percent == 100) mVideoState = VIDEO_STATE.READY;
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                if (mp == mMediaPlayer)
                    mCurrentBufferingPercent = percent;
            }
            mMediaPlayerLock.Unlock();
        }

        public void Dispose()
        {
        }

        /**
         * \brief Callback for interface MediaPlayer.OnCompletionListener
         * @param mediaPlayer
         */
        public void OnCompletion(MediaPlayer mp)
        {
            mVideoState = VIDEO_STATE.END;
        }

        /**
         * \brief Callback for interface MediaPlayer.OnPreparedListener
         * @param mediaPlayer
         */
        public void OnPrepared(MediaPlayer mp)
        {
            mVideoState = VIDEO_STATE.READY;
            // If requested an immediate play
            if (mAutoStart)
                play();
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
                Log.Error("Pikkart AR Video", "Error while opening the file. Unloading the media player (" + errorDescription + ", " + extra + ")");
                unload();
                mVideoState = VIDEO_STATE.ERROR;
                return true;
            }
            return false;
        }

        /**
         * \brief Enum for the possible state of the video player
         */
        public class VideoSate
        {
            public enum VIDEO_STATE
            {
                END = 0,
                PAUSED = 1,
                STOPPED = 2,
                PLAYING = 3,
                READY = 4,
                NOT_READY = 5,
                BUFFERING = 6,
                ERROR = 7
            }
        }

        /**
         * \brief initialization, pretty dumb
         */
        public void init()
        {
            mMediaPlayerLock = new ReentrantLock();
            mSurfaceTextureLock = new ReentrantLock();
        }

        /**
         * \brief Deinitialization, unload stuff and release surface texture.
         */
        public void deinit()
        {
            unload();

            mSurfaceTextureLock.Lock();
            mSurfaceTexture = null;
            mSurfaceTextureLock.Unlock();
        }


        /**
         * \brief load a media file, either from file or from web
         * @param url file path or url (String)
         * @param playFullscreen whatever should play in fullscreen or in AR
         * @param autoStart auto-start when ready
         * @param seekPosition start position (in milliseconds)
         * @return true on success
         */
        public bool load(string url, bool playFullscreen, bool autoStart, int seekPosition)
        {
            mMediaPlayerLock.Lock();
            mSurfaceTextureLock.Lock();

            //if it's in a different state than NOT_READY don't load it. unload() must be called first!
            if ((mVideoState != VIDEO_STATE.NOT_READY) || (mMediaPlayer != null))
            {
                Log.Warn("Pikkart AR Video", "Already loaded");
                mSurfaceTextureLock.Unlock();
                mMediaPlayerLock.Unlock();
                return false;
            }
            //if AR video (not fullscreen) was requested create and set the media player
            //we can play video in AR only if a SurfaceTexture has been created
            if (!playFullscreen && (Build.VERSION.SdkInt >= BuildVersionCodes.IceCreamSandwich) && mSurfaceTexture != null)
            {
                mMediaPlayer = new MediaPlayer();
                //first search for the video locally, then check online
                AssetFileDescriptor afd = null;
                bool fileExist = true;
                try
                {
                    afd = mParentActivity.Assets.OpenFd(url);
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
                        mMovieUrl = url;
                        mMediaPlayer.SetDataSource(afd.FileDescriptor, afd.StartOffset, afd.Length);
                        afd.Close();
                    }
                    else
                    {
                        string URL_REGEX = "^((https?|ftp)://|(www|ftp)\\.)[a-z0-9-]+(\\.[a-z0-9-]+)+((:[0-9]+)|)+([/?].*)?$"; //should be ok
                        Java.Util.Regex.Pattern p = Java.Util.Regex.Pattern.Compile(URL_REGEX);
                        Matcher m = p.Matcher(url);//replace with string to compare
                        if (m.Find())
                        {
                            mMovieUrl = url;
                            mMediaPlayer.SetDataSource(mMovieUrl);
                        }
                    }
                    try
                    {

                        mMediaPlayer.SetOnPreparedListener(this);

                    } catch (Exception e)
                    {
                        System.Diagnostics.Debug.WriteLine(e.StackTrace);
                    }
                    mMediaPlayer.SetOnBufferingUpdateListener(this);
                    mMediaPlayer.SetOnCompletionListener(this);
                    mMediaPlayer.SetOnErrorListener(this);
                    mMediaPlayer.SetAudioStreamType(Stream.Music);
                    mMediaPlayer.SetSurface(new Surface(mSurfaceTexture));
                    mFullscreen = false;
                    mAutoStart = autoStart;
                    mMediaPlayer.PrepareAsync();
                    mSeekPosition = seekPosition;
                }
                catch (IOException e)
                {
                    Log.Error("Pikkart AR Video", "Error while creating the MediaPlayer: " + e.ToString());
                    mMovieUrl = "";
                    mVideoState = VIDEO_STATE.ERROR;
                    mMediaPlayerLock.Unlock();
                    mSurfaceTextureLock.Unlock();
                    return false;
                }
            }
            else
            { //play full screen if requested or old android
                mPlayFullScreenIntent = new Intent(mParentActivity, typeof(FullscreenVideoPlayer));
                mPlayFullScreenIntent.SetAction(Android.Content.Intent.ActionView);
                mFullscreen = true;
                mMovieUrl = url;
                mSeekPosition = seekPosition;
                mVideoState = VIDEO_STATE.READY;
            }

            mSurfaceTextureLock.Unlock();
            mMediaPlayerLock.Unlock();

            return true;
        }


        /**
         * \brief unload a media file, media player and related data
         * @return true on success
         */
        public bool unload()
        {
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
                    Log.Error("Pikkart AR Video", "Could not start playback");
                }

                mMediaPlayer.Release();
                mMediaPlayer = null;
            }
            mMediaPlayerLock.Unlock();

            mVideoState = VIDEO_STATE.NOT_READY;
            mFullscreen = false;
            mAutoStart = false;
            mSeekPosition = -1;
            mMovieUrl = "";
            return true;
        }


        /**
         * \brief get video screen width
         * @return video screen width
         */
        public int getVideoWidth()
        {
            if (mFullscreen)
            {
                Log.Warn("Pikkart AR Video", "cannot get the video width if it is playing fullscreen");
                return -1;
            }

            if ((mVideoState == VIDEO_STATE.NOT_READY) || (mVideoState == VIDEO_STATE.ERROR))
            {
                Log.Warn("Pikkart AR Video", "cannot get the video width if it is not ready");
                return -1;
            }

            int result = -1;
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                result = mMediaPlayer.VideoWidth;
            }
            mMediaPlayerLock.Unlock();

            return result;
        }


        /**
         * \brief get video screen height
         * @return video screen height
         */
        public int getVideoHeight()
        {
            if (mFullscreen)
            {
                Log.Warn("Pikkart AR Video", "cannot get the video height if it is playing fullscreen");
                return -1;
            }

            if ((mVideoState == VIDEO_STATE.NOT_READY) || (mVideoState == VIDEO_STATE.ERROR))
            {
                Log.Warn("Pikkart AR Video", "cannot get the video height if it is not ready");
                return -1;
            }

            int result = -1;
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                result = mMediaPlayer.VideoHeight;
            }
            mMediaPlayerLock.Unlock();

            return result;
        }


        /**
         * \brief get video duration
         * @return video duration
         */
        public int getLength()
        {
            if (mFullscreen)
            {
                Log.Warn("Pikkart AR Video", "cannot get the video length if it is playing fullscreen");
                return -1;
            }

            if ((mVideoState == VIDEO_STATE.NOT_READY) || (mVideoState == VIDEO_STATE.ERROR))
            {
                Log.Warn("Pikkart AR Video", "cannot get the video length if it is not ready");
                return -1;
            }

            int result = -1;
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                result = mMediaPlayer.Duration;
            }
            mMediaPlayerLock.Unlock();

            return result;
        }


        /**
         * \brief request the video to be played
         * @return true on success
         */
        public bool play()
        {
            if (mFullscreen)
            {
                mPlayFullScreenIntent.PutExtra("autostart", true);
                if (mSeekPosition != -1) mPlayFullScreenIntent.PutExtra("seekposition", mSeekPosition);
                mPlayFullScreenIntent.PutExtra("requestedorientation", (int) ScreenOrientation.Landscape);
                mPlayFullScreenIntent.PutExtra("movieurl", mMovieUrl);
                mParentActivity.StartActivity(mPlayFullScreenIntent);
                return true;
            }
            else
            {
                if ((mVideoState == VIDEO_STATE.NOT_READY) || (mVideoState == VIDEO_STATE.ERROR))
                {
                    Log.Warn("Pikkart AR Video", "cannot play this video if it is not ready");
                    return false;
                }
                mMediaPlayerLock.Lock();
                if (mSeekPosition != -1)
                {
                    try
                    {
                        mMediaPlayer.SeekTo(mSeekPosition);
                    }
                    catch (Exception e) { }
                }
                else
                {
                    try
                    {
                        mMediaPlayer.SeekTo(0);
                    }
                    catch (Exception e) { }
                }
                try
                {
                    mMediaPlayer.Start();
                }
                catch (Exception e)
                {
                    Log.Error("Pikkart AR Video", "could not start playback");
                }
                mVideoState = VIDEO_STATE.PLAYING;
                mMediaPlayerLock.Unlock();
                return true;
            }
        }


        /**
         * \brief pauses the current movie being played
         * @return true on success
         */
        public bool pause()
        {
            if (mFullscreen)
            {
                Log.Warn("Pikkart AR Video", "cannot pause this video since it is fullscreen");
                return false;
            }
            if ((mVideoState == VIDEO_STATE.NOT_READY) || (mVideoState == VIDEO_STATE.ERROR))
            {
                Log.Warn("Pikkart AR Video", "cannot pause this video if it is not ready");
                return false;
            }
            bool result = false;
            mMediaPlayerLock.Lock() ;
            if (mMediaPlayer != null)
            {
                if (mMediaPlayer.IsPlaying)
                {
                    try
                    {
                        mMediaPlayer.Pause();
                    }
                    catch (Exception e)
                    {
                        mMediaPlayerLock.Unlock();
                        Log.Error("Pikkart AR Video", "could not pause playback");
                    }
                    mVideoState = VIDEO_STATE.PAUSED;
                    result = true;
                }
            }
            mMediaPlayerLock.Unlock();
            return result;
        }


        /**
         * \brief stop the current movie being played
         * @return true on success
         */
        public bool stop()
        {
            if (mFullscreen)
            {
                Log.Debug("Pikkart AR Video", "cannot stop this video since it is not on texture");
                return false;
            }
            if ((mVideoState == VIDEO_STATE.NOT_READY) || (mVideoState == VIDEO_STATE.ERROR))
            {
                Log.Debug("Pikkart AR Video", "cannot stop this video if it is not ready");
                return false;
            }
            bool result = false;
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                mVideoState = VIDEO_STATE.STOPPED;
                try
                {
                    mMediaPlayer.Stop();
                }
                catch (Exception e)
                {
                    mMediaPlayerLock.Unlock();
                    Log.Error("Pikkart AR Video", "Could not stop playback");
                }
                result = true;
            }
            mMediaPlayerLock.Unlock();
            return result;
        }

        /**
         * \brief update the surface texture with new video data
         * @return OpenGL texture id assigned to the surfacetexture
         */
        public byte updateVideoData()
        {
            if (mFullscreen)
            {
                return 0;
            }
            byte result = 0;
            mSurfaceTextureLock.Lock();
            if (mSurfaceTexture != null)
            {
                if (mVideoState == VIDEO_STATE.PLAYING)
                    mSurfaceTexture.UpdateTexImage();
                result = mTextureID;
            }
            mSurfaceTextureLock.Unlock();
            return result;
        }


        /**
         * \brief move video playback to seek position
         * @param position seek to position (in milliseconds)
         * @return true on success
         */
        public bool seekTo(int position)
        {
            if (mFullscreen)
            {
                Log.Debug("Pikkart AR Video", "cannot seek-to on this video since it is fullscreen");
                return false;
            }
            if ((mVideoState == VIDEO_STATE.NOT_READY) || (mVideoState == VIDEO_STATE.ERROR))
            {
                Log.Debug("Pikkart AR Video", "cannot seek-to on this video if it is not ready");
                return false;
            }
            bool result = false;
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                try
                {
                    mMediaPlayer.SeekTo(position);
                }
                catch (Exception e)
                {
                    mMediaPlayerLock.Unlock();
                    Log.Error("Pikkart AR Video", "could not seek to position");
                }
                result = true;
            }
            mMediaPlayerLock.Unlock();
            return result;
        }


        /**
         * \brief get current playback position
         * @return playback position (in milliseconds)
         */
        public int getCurrentPosition()
        {
            if (mFullscreen)
            {
                return -1;
            }
            if ((mVideoState == VIDEO_STATE.NOT_READY) || (mVideoState == VIDEO_STATE.ERROR))
            {
                return -1;
            }
            int result = -1;
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                result = mMediaPlayer.CurrentPosition;
            }
            mMediaPlayerLock.Unlock();
            return result;
        }

        /**
         * \brief set video volume
         * @param value volume (0.0 to 1.0)
         * @return true on success
         */
        public bool setVolume(float value)
        {
            if (mFullscreen)
            {
                return false;
            }
            if ((mVideoState == VIDEO_STATE.NOT_READY) || (mVideoState == VIDEO_STATE.ERROR))
            {
                return false;
            }
            bool result = false;
            mMediaPlayerLock.Lock();
            if (mMediaPlayer != null)
            {
                mMediaPlayer.SetVolume(value, value);
                result = true;
            }
            mMediaPlayerLock.Unlock();
            return result;
        }

        /**
         * \brief set the surfacetexture with a given OpenGL texture id
         * @param TextureID the opengl texture id
         * @return true on success
         */
        public bool setupSurfaceTexture(int TextureID)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.IceCreamSandwich)
            {
                mSurfaceTextureLock.Lock();
                mSurfaceTexture = new SurfaceTexture(TextureID);
                mTextureID = (byte)TextureID;
                mSurfaceTextureLock.Unlock();
                return true;
            }
            else
            {
                return false;
            }
        }

        /**
         * \brief get the surface texture transformation matrix (used to transform texture coordinates in OpenGL)
         * @param mtx the float array where to store matrix data
         */
        public void getSurfaceTextureTransformMatrix(float[] mtx)
        {
            mSurfaceTextureLock.Lock();
            if (mSurfaceTexture != null)
                mSurfaceTexture.GetTransformMatrix(mtx);
            mSurfaceTextureLock.Unlock();
        }
    }
}
