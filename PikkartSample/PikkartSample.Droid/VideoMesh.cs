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
using Java.Nio;
using Android.Content.Res;
using Android.Opengl;
using Com.Pikkart.AR.Recognition.Items;
using Com.Pikkart.AR.Recognition;

namespace PikkartSample.Droid
{
    /**
     * \class VideoMesh
     * \brief A Video Mesh: a plane, a special video texture and a video player all in one.
     */
    public class VideoMesh
    {

        private Activity mParentActivity = null; /**< the activity holding this mesh/video player */

        private String mName; /**< mesh name */
        /**
         * \brief Get this mesh name.
         * @return the mesh name as a String.
         */
        public String Name() { return mName; }

        private ByteBuffer mVertices_Buffer; /**< vertices data */
        private ByteBuffer mTexCoords_Buffer; /**< texture coordinates data */
        private ByteBuffer mVideoTexCoords_Buffer; /**< video texture coordinates data */
        private ByteBuffer mNormals_Buffer; /**< normals data */
        private ByteBuffer mIndex_Buffer; /**< triangle indices data */

        private int mIndices_Number = 0; /**< number of indices */
        private int mVertices_Number = 0; /**< number of vertices */

        private int mKeyframeTexture_GL_ID = 0; /**< video keyframe texture opengl id */
        private int mIconBusyTexture_GL_ID = 0; /**< busy icon texture opengl id */
        private int mIconPlayTexture_GL_ID = 0; /**< play icon texture opengl id */
        private int mIconErrorTexture_GL_ID = 0; /**< error icon texture opengl id */
        private int mVideoTexture_GL_ID = 0; /**< busy icon texture opengl id */

        private int mVideo_Program_GL_ID = 0; /**< video shader program opengl id */
        private int mKeyframe_Program_GL_ID = 0; /**< icons and keyframe shader program opengl id */

        private PikkartVideoPlayer mPikkartVideoPlayer = null; /**< the AR video player */
        private String mMovieUrl = ""; /**< the video URL (ot file path<) */
        private int mSeekPosition = 0; /**< starting position (in millesconds) */
        private bool mAutostart = false; /**< mesh shader program opengl id */

        private float keyframeAspectRatio = 1.0f; /**< aspect ration of the keyframe image */
        private float videoAspectRatio = 1.0f; /**< aspect ratio of the video */

        private float[] mTexCoordTransformationMatrix = null; /**< trasnformation matrix for the video texture coords */

        /**
         * texture coordinates of the video
         */
        private float[] videoTextureCoords = {  0.0f, 1.0f,
                                                1.0f, 1.0f,
                                                1.0f, 0.0f,
                                                0.0f, 0.0f, };


        /**
         * transformed texture coordinates of the video
         */
        private float[] videoTextureCoordsTransformed = {   0.0f, 0.0f,
                                                            1.0f, 0.0f,
                                                            1.0f, 1.0f,
                                                            0.0f, 1.0f, };


        /**
         * This mesh vertex shader code. A very basic vetex shader
         */
        public const String VERTEX_SHADER = " \n" + "\n"
            + "attribute vec4 vertexPosition; \n"
            + "attribute vec2 vertexTexCoord; \n" + "\n"
            + "varying vec2 texCoord; \n" + "\n"
            + "uniform mat4 modelViewProjectionMatrix; \n" + "\n"
            + "void main() { \n"
            + "   gl_Position = modelViewProjectionMatrix * vertexPosition; \n"
            + "   texCoord = vertexTexCoord; \n"
            + "} \n";

        /**
         * This mesh fragment shader code for the icons and keyframe. A very basic fragment shader
         */
        public const String KEYFRAME_FRAGMENT_SHADER = " \n" + "\n"
                + "precision mediump float; \n" + " \n"
                + "varying vec2 texCoord; \n" + " \n"
                + "uniform sampler2D texSampler2D; \n" + " \n"
                + "void main() { \n"
                + "   gl_FragColor = texture2D(texSampler2D, texCoord); \n"
                + "} \n";

        /**
         * This mesh fragment shader codefor the video texture. A very basic fragment shader
         */
        public const String VIDEO_FRAGMENT_SHADER = " \n"
                + "#extension GL_OES_EGL_image_external : require \n"
                + "precision mediump float; \n"
                + "varying vec2 texCoord; \n"
                + "uniform samplerExternalOES texSamplerOES; \n" + " \n"
                + "void main() { \n"
                + "   gl_FragColor = texture2D(texSamplerOES, texCoord); \n"
                + "} \n";

        /**
         * \brief Generate a ByteBuffer from a float array.
         * @param array the float array.
         * @return the generated ByteBuffer.
         */
        protected ByteBuffer FillBuffer(float[] array)
        {
            ByteBuffer bb = ByteBuffer.AllocateDirect(4 * array.Length);
            bb.Order(ByteOrder.LittleEndian);
            foreach (float d in array)
            {
                bb.PutFloat(d);
            }
            bb.Rewind();
            return bb;
        }

        /**
         * \brief Generate a ByteBuffer from a short array.
         * @param array the short array.
         * @return the generated ByteBuffer.
         */
        protected ByteBuffer FillBuffer(short[] array)
        {
            ByteBuffer bb = ByteBuffer.AllocateDirect(2 * array.Length);
            bb.Order(ByteOrder.LittleEndian);
            foreach (short s in array)
            {
                bb.PutShort(s);
            }
            bb.Rewind();
            return bb;
        }

        /**
         * \brief Generate the video mesh geometrical data.
         * @return true on success.
         */
        private bool GenerateMesh()
        {
            float[] verticesArray = { 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f,
                1.0f, 0.0f, 0.0f, 1.0f, 0.0f };
            mVertices_Buffer = FillBuffer(verticesArray);
            mVertices_Number = 4;

            float[] texCoordsArray = { 0.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f,
                0.0f };
            mTexCoords_Buffer = FillBuffer(texCoordsArray);

            float[] normalsArray = { 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f, 0.0f,
                0.0f, 1.0f, 0.0f, 0.0f, 1.0f};
            mNormals_Buffer = FillBuffer(normalsArray);

            short[] indicesArray = { 0, 1, 2, 2, 3, 0 };
            mIndex_Buffer = FillBuffer(indicesArray);
            mIndices_Number = 6;

            return true;
        }


        /**
         * \brief Constructor.
         * @param parent the parent activity.
         */
        public VideoMesh(Activity parent)
        {
            mParentActivity = parent;
        }

        /**
         * \brief Set to use an external video player.
         *
         * Call this before InitMesh, only if you want to use an external PikkartVideoPlayer
         * @param pikkartVideoPlayer the external PikkartVideoPlayer object.
         */
        public void SetVideoPlayer(PikkartVideoPlayer pikkartVideoPlayer)
        {
            mPikkartVideoPlayer = pikkartVideoPlayer;
        }

        /**
         * \brief Initialize a VideoMesh object.
         *
         * Intialize a VideMesh object, if necessary creates its own PikkartVideoPlayer.
         * @param am the app AssetManager.
         * @param movieUrl the video to be played, URL or file path
         * @param keyframeUrl the video keyframe image to be displayed
         * @param seekPosition the starting position (in milliseconds)
         * @param autostart whatever the video should autostart on detection
         * @param pikkartVideoPlayer an external PikkartVideoPlayer to use. If null, an internal one will be created.
         * @return true on success.
         */
        public bool InitMesh(AssetManager am,
                                String movieUrl,
                                String keyframeUrl,
                                int seekPosition,
                                bool autostart,
                                PikkartVideoPlayer pikkartVideoPlayer)
        {
            GenerateMesh();
            if (pikkartVideoPlayer == null)
            {
                mPikkartVideoPlayer = new PikkartVideoPlayer();
                mPikkartVideoPlayer.init();
                mPikkartVideoPlayer.setActivity(mParentActivity);
            }
            else
            {
                mPikkartVideoPlayer = pikkartVideoPlayer;
            }
            mMovieUrl = movieUrl;
            int[] dims = new int[2];
            mKeyframeTexture_GL_ID = RenderUtils.loadTextureFromApk(am, keyframeUrl, dims);
            keyframeAspectRatio = (float)dims[1] / (float)dims[0];

            mSeekPosition = seekPosition;
            mAutostart = autostart;

            mIconBusyTexture_GL_ID = RenderUtils.loadTextureFromApk(am, "media/busy.png");
            mIconPlayTexture_GL_ID = RenderUtils.loadTextureFromApk(am, "media/play.png");
            mIconErrorTexture_GL_ID = RenderUtils.loadTextureFromApk(am, "media/error.png");

            mKeyframe_Program_GL_ID = RenderUtils.createProgramFromShaderSrc(VERTEX_SHADER, KEYFRAME_FRAGMENT_SHADER);
            mVideo_Program_GL_ID = RenderUtils.createProgramFromShaderSrc(VERTEX_SHADER, VIDEO_FRAGMENT_SHADER);

            mVideoTexture_GL_ID = RenderUtils.createVideoTexture();

            bool canFullscreen = true;
            if (mPikkartVideoPlayer != null)
            {
                if (mPikkartVideoPlayer.setupSurfaceTexture(mVideoTexture_GL_ID))
                {
                    canFullscreen = false;
                }
                mPikkartVideoPlayer.load(mMovieUrl, canFullscreen, mAutostart, mSeekPosition);
            }

            mTexCoordTransformationMatrix = new float[16];

            return true;
        }

        /**
         * \brief Reload the video
         */
        public void ReloadOnResume()
        {
            bool canFullscreen = true;
            if (mPikkartVideoPlayer != null)
            {
                if (!mPikkartVideoPlayer.setupSurfaceTexture(mVideoTexture_GL_ID))
                {
                    canFullscreen = false;
                }
                mPikkartVideoPlayer.load(mMovieUrl, canFullscreen, mAutostart, mSeekPosition);
            }
        }

        /**
         * \brief Pause the video
         */
        public void PauseVideo()
        {
            if (mPikkartVideoPlayer != null)
            {
                PikkartVideoPlayer.VideoSate.VIDEO_STATE status = mPikkartVideoPlayer.getVideoStatus();
                if (status == PikkartVideoPlayer.VideoSate.VIDEO_STATE.PLAYING)
                {
                    mPikkartVideoPlayer.pause();
                }
            }
        }

        /**
         * \brief Play/Pause the video
         */
        public void PlayOrPauseVideo()
        {
            if (mPikkartVideoPlayer != null)
            {
                PikkartVideoPlayer.VideoSate.VIDEO_STATE status = mPikkartVideoPlayer.getVideoStatus();
                if (status == PikkartVideoPlayer.VideoSate.VIDEO_STATE.PLAYING)
                {
                    mPikkartVideoPlayer.pause();
                }
                else if (status == PikkartVideoPlayer.VideoSate.VIDEO_STATE.END ||
                        status == PikkartVideoPlayer.VideoSate.VIDEO_STATE.PAUSED ||
                        status == PikkartVideoPlayer.VideoSate.VIDEO_STATE.READY ||
                        status == PikkartVideoPlayer.VideoSate.VIDEO_STATE.STOPPED)
                {
                    mPikkartVideoPlayer.play();
                }
            }
        }

        /**
         * \brief Regenerate and transform video texture coordinates
         * @param videoWidth video screen width.
         * @param videoHeight video screen height.
         * @param textureCoordMatrix the transfomr matrix for the video texture coordinates
         */
        private void SetVideoDimensions(float videoWidth, float videoHeight, float[] textureCoordMatrix)
        {
            videoAspectRatio = videoHeight / videoWidth;

            float[] mtx = textureCoordMatrix;
            float[] tempUVMultRes = null;

            tempUVMultRes = RenderUtils.uvMultMat4f(videoTextureCoords[0], videoTextureCoords[1], mtx);
            videoTextureCoordsTransformed[0] = tempUVMultRes[0];
            videoTextureCoordsTransformed[1] = tempUVMultRes[1];

            tempUVMultRes = RenderUtils.uvMultMat4f(videoTextureCoords[2], videoTextureCoords[3], mtx);
            videoTextureCoordsTransformed[2] = tempUVMultRes[0];
            videoTextureCoordsTransformed[3] = tempUVMultRes[1];

            tempUVMultRes = RenderUtils.uvMultMat4f(videoTextureCoords[4], videoTextureCoords[5], mtx);
            videoTextureCoordsTransformed[4] = tempUVMultRes[0];
            videoTextureCoordsTransformed[5] = tempUVMultRes[1];

            tempUVMultRes = RenderUtils.uvMultMat4f(videoTextureCoords[6], videoTextureCoords[7], mtx);
            videoTextureCoordsTransformed[6] = tempUVMultRes[0];
            videoTextureCoordsTransformed[7] = tempUVMultRes[1];
        }

        /**
         * \brief Draw the video keyframe (in OpenGL).
         * @param mvpMatrix the model-view-projection matrix.
         */
        private void DrawKeyFrame(float[] mvpMatrix)
        {
            GLES20.GlEnable(GLES20.GlBlend);
            GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);

            GLES20.GlUseProgram(mKeyframe_Program_GL_ID);

            int vertexHandle = GLES20.GlGetAttribLocation(mKeyframe_Program_GL_ID, "vertexPosition");
            int textureCoordHandle = GLES20.GlGetAttribLocation(mKeyframe_Program_GL_ID, "vertexTexCoord");
            int mvpMatrixHandle = GLES20.GlGetUniformLocation(mKeyframe_Program_GL_ID, "modelViewProjectionMatrix");
            int texSampler2DHandle = GLES20.GlGetUniformLocation(mKeyframe_Program_GL_ID, "texSampler2D");

            GLES20.GlVertexAttribPointer(vertexHandle, 3, GLES20.GlFloat, false, 0, mVertices_Buffer);
            GLES20.GlVertexAttribPointer(textureCoordHandle, 2, GLES20.GlFloat, false, 0, mTexCoords_Buffer);

            GLES20.GlEnableVertexAttribArray(vertexHandle);
            GLES20.GlEnableVertexAttribArray(textureCoordHandle);

            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlBindTexture(GLES20.GlTexture2d, mKeyframeTexture_GL_ID);
            GLES20.GlUniform1i(texSampler2DHandle, 0);

            GLES20.GlUniformMatrix4fv(mvpMatrixHandle, 1, false, mvpMatrix, 0);


            GLES20.GlDrawElements(GLES20.GlTriangles, mIndices_Number, GLES20.GlUnsignedShort, mIndex_Buffer);

            GLES20.GlDisableVertexAttribArray(vertexHandle);
            GLES20.GlDisableVertexAttribArray(textureCoordHandle);

            GLES20.GlUseProgram(0);
            GLES20.GlDisable(GLES20.GlBlend);
        }

        /**
         * \brief Draw the video (in OpenGL).
         * @param mvpMatrix the model-view-projection matrix.
         */
        private void DrawVideo(float[] mvpMatrix)
        {
            GLES20.GlUseProgram(mVideo_Program_GL_ID);

            int vertexHandle = GLES20.GlGetAttribLocation(mKeyframe_Program_GL_ID, "vertexPosition");
            int textureCoordHandle = GLES20.GlGetAttribLocation(mKeyframe_Program_GL_ID, "vertexTexCoord");
            int mvpMatrixHandle = GLES20.GlGetUniformLocation(mKeyframe_Program_GL_ID, "modelViewProjectionMatrix");
            int texSampler2DHandle = GLES20.GlGetUniformLocation(mKeyframe_Program_GL_ID, "texSamplerOES");

            GLES20.GlVertexAttribPointer(vertexHandle, 3, GLES20.GlFloat, false, 0, mVertices_Buffer);
            GLES20.GlVertexAttribPointer(textureCoordHandle, 2, GLES20.GlFloat, false, 0, mVideoTexCoords_Buffer);

            GLES20.GlEnableVertexAttribArray(vertexHandle);
            GLES20.GlEnableVertexAttribArray(textureCoordHandle);

            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlBindTexture(GLES11Ext.GlTextureExternalOes, mVideoTexture_GL_ID);
            GLES20.GlUniform1i(texSampler2DHandle, 0);


            GLES20.GlUniformMatrix4fv(mvpMatrixHandle, 1, false, mvpMatrix, 0);

            // Render
            GLES20.GlDrawElements(GLES20.GlTriangles, mIndices_Number, GLES20.GlUnsignedShort, mIndex_Buffer);

            GLES20.GlDisableVertexAttribArray(vertexHandle);
            GLES20.GlDisableVertexAttribArray(textureCoordHandle);

            GLES20.GlUseProgram(0);
        }

        /**
         * \brief Draw the video icon (in OpenGL).
         * @param mvpMatrix the model-view-projection matrix.
         * @param status the video state.
         */
        private void DrawIcon(float[] mvpMatrix, PikkartVideoPlayer.VideoSate.VIDEO_STATE status)
        {
            GLES20.GlEnable(GLES20.GlBlend);
            GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);

            GLES20.GlUseProgram(mKeyframe_Program_GL_ID);

            int vertexHandle = GLES20.GlGetAttribLocation(mKeyframe_Program_GL_ID, "vertexPosition");
            int textureCoordHandle = GLES20.GlGetAttribLocation(mKeyframe_Program_GL_ID, "vertexTexCoord");
            int mvpMatrixHandle = GLES20.GlGetUniformLocation(mKeyframe_Program_GL_ID, "modelViewProjectionMatrix");
            int texSampler2DHandle = GLES20.GlGetUniformLocation(mKeyframe_Program_GL_ID, "texSampler2D");

            GLES20.GlVertexAttribPointer(vertexHandle, 3, GLES20.GlFloat, false, 0, mVertices_Buffer);
            GLES20.GlVertexAttribPointer(textureCoordHandle, 2, GLES20.GlFloat, false, 0, mTexCoords_Buffer);

            GLES20.GlEnableVertexAttribArray(vertexHandle);
            GLES20.GlEnableVertexAttribArray(textureCoordHandle);

            GLES20.GlActiveTexture(GLES20.GlTexture0);
            switch ((int)status)
            {
                case 0://end
                    GLES20.GlBindTexture(GLES20.GlTexture2d, mIconPlayTexture_GL_ID);
                    break;
                case 1://pasued
                    GLES20.GlBindTexture(GLES20.GlTexture2d, mIconPlayTexture_GL_ID);
                    break;
                case 2://stopped
                    GLES20.GlBindTexture(GLES20.GlTexture2d, mIconPlayTexture_GL_ID);
                    break;
                case 3://playing
                    GLES20.GlBindTexture(GLES20.GlTexture2d, mIconPlayTexture_GL_ID);
                    break;
                case 4://ready
                    GLES20.GlBindTexture(GLES20.GlTexture2d, mIconPlayTexture_GL_ID);
                    break;
                case 5://not ready
                    GLES20.GlBindTexture(GLES20.GlTexture2d, mIconBusyTexture_GL_ID);
                    break;
                case 6://buffering
                    GLES20.GlBindTexture(GLES20.GlTexture2d, mIconBusyTexture_GL_ID);
                    break;
                case 7://error
                    GLES20.GlBindTexture(GLES20.GlTexture2d, mIconErrorTexture_GL_ID);
                    break;
                default:
                    GLES20.GlBindTexture(GLES20.GlTexture2d, mIconBusyTexture_GL_ID);
                    break;
            }
            GLES20.GlUniform1i(texSampler2DHandle, 0);

            GLES20.GlUniformMatrix4fv(mvpMatrixHandle, 1, false, mvpMatrix, 0);


            GLES20.GlDrawElements(GLES20.GlTriangles, mIndices_Number, GLES20.GlUnsignedShort, mIndex_Buffer);

            GLES20.GlDisableVertexAttribArray(vertexHandle);
            GLES20.GlDisableVertexAttribArray(textureCoordHandle);

            GLES20.GlUseProgram(0);
            GLES20.GlDisable(GLES20.GlBlend);
        }

        /**
         * \brief Draw the video mesh /with keyframe and icons too) (in OpenGL).
         * @param modelView the model-view matrix.
         * @param projection the projection matrix.
         */
        public void DrawMesh(float[] modelView, float[] projection)
        {
            PikkartVideoPlayer.VideoSate.VIDEO_STATE currentStatus = PikkartVideoPlayer.VideoSate.VIDEO_STATE.NOT_READY;
            if (mPikkartVideoPlayer != null)
            {
                currentStatus = mPikkartVideoPlayer.getVideoStatus();
                if (!mPikkartVideoPlayer.isFullscreen())
                {
                    if (mPikkartVideoPlayer.getVideoStatus() == PikkartVideoPlayer.VideoSate.VIDEO_STATE.PLAYING)
                    {
                        mPikkartVideoPlayer.updateVideoData();
                    }
                    mPikkartVideoPlayer.getSurfaceTextureTransformMatrix(mTexCoordTransformationMatrix);
                    SetVideoDimensions(mPikkartVideoPlayer.getVideoWidth(), mPikkartVideoPlayer.getVideoHeight(), mTexCoordTransformationMatrix);
                    mVideoTexCoords_Buffer = FillBuffer(videoTextureCoordsTransformed);
                }
            }

            Marker currentMarker = RecognitionFragment.CurrentMarker;
            if (currentMarker != null)
            {
                float markerWidth = currentMarker.Width;
                float markerHeight = currentMarker.Height;

                GLES20.GlEnable(GLES20.GlDepthTest);
                //GLES20.GlDisable(GLES20.GlCullFaceMode);
                GLES20.GlCullFace(GLES20.GlBack);
                GLES20.GlFrontFace(GLES20.GlCw);

                if ((currentStatus == PikkartVideoPlayer.VideoSate.VIDEO_STATE.READY)
                        || (currentStatus == PikkartVideoPlayer.VideoSate.VIDEO_STATE.END)
                        || (currentStatus == PikkartVideoPlayer.VideoSate.VIDEO_STATE.NOT_READY)
                        || (currentStatus == PikkartVideoPlayer.VideoSate.VIDEO_STATE.ERROR))
                {

                    float[] scaleMatrix = new float[16];
                    RenderUtils.matrix44Identity(scaleMatrix);
                    scaleMatrix[0] = markerWidth;
                    scaleMatrix[5] = markerWidth * keyframeAspectRatio;
                    scaleMatrix[10] = markerWidth;

                    float[] temp_mv = new float[16];
                    RenderUtils.matrixMultiply(4, 4, modelView, 4, 4, scaleMatrix, temp_mv);

                    float[] temp_mvp = new float[16];
                    RenderUtils.matrixMultiply(4, 4, projection, 4, 4, temp_mv, temp_mvp);
                    float[] mvpMatrix = new float[16];
                    RenderUtils.matrix44Transpose(temp_mvp, mvpMatrix);

                    DrawKeyFrame(mvpMatrix);
                }
                else
                {
                    float[] scaleMatrix = new float[16];
                    RenderUtils.matrix44Identity(scaleMatrix);
                    scaleMatrix[0] = markerWidth;
                    scaleMatrix[5] = markerWidth * videoAspectRatio;
                    scaleMatrix[10] = markerWidth;

                    float[] temp_mv = new float[16];
                    RenderUtils.matrixMultiply(4, 4, modelView, 4, 4, scaleMatrix, temp_mv);

                    float[] temp_mvp = new float[16];
                    RenderUtils.matrixMultiply(4, 4, projection, 4, 4, temp_mv, temp_mvp);
                    float[] mvpMatrix = new float[16];
                    RenderUtils.matrix44Transpose(temp_mvp, mvpMatrix);

                    DrawVideo(mvpMatrix);
                }

                if ((currentStatus == PikkartVideoPlayer.VideoSate.VIDEO_STATE.READY)
                        || (currentStatus == PikkartVideoPlayer.VideoSate.VIDEO_STATE.END)
                        || (currentStatus == PikkartVideoPlayer.VideoSate.VIDEO_STATE.PAUSED)
                        || (currentStatus == PikkartVideoPlayer.VideoSate.VIDEO_STATE.NOT_READY)
                        || (currentStatus == PikkartVideoPlayer.VideoSate.VIDEO_STATE.ERROR))
                {

                    float[] translateMatrix = new float[16];
                    RenderUtils.matrix44Identity(translateMatrix);
                    //scale a bit
                    translateMatrix[0] = 0.4f;
                    translateMatrix[5] = 0.4f;
                    translateMatrix[10] = 0.4f;
                    //translate a bit
                    translateMatrix[3] = 0.0f;
                    translateMatrix[7] = 0.45f;
                    translateMatrix[11] = -0.05f;

                    float[] temp_mv = new float[16];
                    RenderUtils.matrixMultiply(4, 4, modelView, 4, 4, translateMatrix, temp_mv);

                    float[] temp_mvp = new float[16];
                    RenderUtils.matrixMultiply(4, 4, projection, 4, 4, temp_mv, temp_mvp);
                    float[] mvpMatrix = new float[16];
                    RenderUtils.matrix44Transpose(temp_mvp, mvpMatrix);

                    DrawIcon(mvpMatrix, currentStatus);
                }
                RenderUtils.CheckGLError("VideoMesh:end video renderer");
            }
        }

        /**
         * \brief Is the media player playing the video
         * @return true if playing
         */
        public bool IsPlaying()
        {
            if (mPikkartVideoPlayer != null)
            {
                if (mPikkartVideoPlayer.getVideoStatus() == PikkartVideoPlayer.VideoSate.VIDEO_STATE.PLAYING ||
                        mPikkartVideoPlayer.getVideoStatus() == PikkartVideoPlayer.VideoSate.VIDEO_STATE.PAUSED)
                {
                    return true;
                }
            }
            return false;
        }
    }
}