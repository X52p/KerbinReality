using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;


namespace KerbalVR
{
    // Start plugin on entering the Flight scene
    //
    [KSPAddon(KSPAddon.Startup.Instantly, false)]
    public class KerbalVRPlugin : MonoBehaviour
    {
        static Vessel activeVessel;
        static RenderSlave leftSlave;
        static RenderSlave rightSlave;

        static copySlave skyCopySlave;


        public static List<Camera> leftCameras = new List<Camera>();
        public static List<Camera> rightCameras = new List<Camera>();

        public static List<GameObject> allCamerasGameObject = new List<GameObject>();

        public bool left = false;
        int eyeDiference = 100; //TODO calculate corectly


        private static object mutex = new object();

        public static Camera camLeft_Interior, camRight_Interior;
        public static Camera camLeft_Near, camLeft_Far;
        public static Camera camRight_Near, camRight_Far;
        public static Camera leftSky, leftStars, rightSky, rightStars;
        public static Camera O_SclaledSpace, O_Galaxy, O_Near, O_Far, O_Interior;

        public static List<GameObject> interiorModelList;

        public static bool leftReady = false;
        public static bool rightReady = false;

        private bool renderToScreen = true;

        private static CVRSystem vrSystem;
        private static CVRCompositor vrCompositor;

        private static RenderTexture hmdLeftEyeRenderTexture, hmdRightEyeRenderTexture, skyTexture;



        private VRControllerState_t ctrlStateLeft = new VRControllerState_t();
        private VRControllerState_t ctrlStateRight = new VRControllerState_t();
        private uint ctrlStateLeft_lastPacketNum, ctrlStateRight_lastPacketNum;


        public static Texture_t hmdLeftEyeTexture, hmdRightEyeTexture;

        private Texture2D myTexture2D;



        private System.Timers.Timer initTmr = new System.Timers.Timer(1000);

        // define controller button masks
        //--------------------------------------------------------------

        // trigger button
        public const ulong CONTROLLER_BUTTON_MASK_TRIGGER = 1ul << (int)EVRButtonId.k_EButton_SteamVR_Trigger;

        // app menu button
        public const ulong CONTROLLER_BUTTON_MASK_APP_MENU = 1ul << (int)EVRButtonId.k_EButton_ApplicationMenu;

        // touchpad
        public const ulong CONTROLLER_BUTTON_MASK_TOUCHPAD = 1ul << (int)EVRButtonId.k_EButton_SteamVR_Touchpad;

        // list of all cameras in the game
        //--------------------------------------------------------------
        private string[] cameraNames = new string[7]
        {
        "GalaxyCamera",
        "Camera ScaledSpace",
        "Camera 01",
        "Camera 00",
        "InternalCamera",
        "UIMainCamera",
        "UIVectorCamera",
        };

        // list of cameras to render (string names), defined on Start()
        private List<string> cameraNamesToRender;

        // struct to keep track of Camera properties
        private struct CameraProperties
        {
            public Camera camera;
            public Matrix4x4 originalProjMatrix;
            public Matrix4x4 hmdLeftProjMatrix;
            public Matrix4x4 hmdRightProjMatrix;

            public CameraProperties(Camera camera, Matrix4x4 originalProjMatrix, Matrix4x4 hmdLeftProjMatrix, Matrix4x4 hmdRightProjMatrix)
            {
                this.camera = camera;
                this.originalProjMatrix = originalProjMatrix;
                this.hmdLeftProjMatrix = hmdLeftProjMatrix;
                this.hmdRightProjMatrix = hmdRightProjMatrix;
            }
        }

        // list of cameras to render (Camera objects)
        private List<CameraProperties> camerasToRender;

        private TrackedDevicePose_t[] vrDevicePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private TrackedDevicePose_t[] vrRenderPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private TrackedDevicePose_t[] vrGamePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        private uint ctrlIndexLeft = 0;
        private uint ctrlIndexRight = 0;
        public static float predict = 0.05f;

        private class copySlave : MonoBehaviour
        {
            // public RenderTexture leftTarget, rightTarget;
            public float u, v;

            void OnRenderImage(RenderTexture r, RenderTexture r2)
            {
                // Graphics.CopyTexture(r, 0, 0, 0, 0, hmdLeftEyeRenderTexture.width, hmdLeftEyeRenderTexture.height, hmdLeftEyeRenderTexture, 0, 0, 0, 0);
                //Graphics.CopyTexture(r, 0, 0, difrence, 0, hmdRightEyeRenderTexture.width, hmdRightEyeRenderTexture.height, hmdRightEyeRenderTexture, 0, 0, 0, 0);
                //   log("cpy");
                Graphics.Blit(r, hmdLeftEyeRenderTexture);
                //Graphics.Blit(r, hmdRightEyeRenderTexture);

            }
        }

        private class RenderSlave : MonoBehaviour
        {
            EVRCompositorError lastError = EVRCompositorError.None;

            public bool HmdOn = false;
            public bool left = false;

            private static VRTextureBounds_t hmdTextureBounds;

            void Start()
            {

                hmdTextureBounds.uMin = 0.0f;
                hmdTextureBounds.uMax = 1.0f;
                hmdTextureBounds.vMin = 1.0f;
                hmdTextureBounds.vMax = 0.0f;
            }

            public static bool leftReady = false;
            public static bool rightReady = false;

            void OnRenderImage(RenderTexture r, RenderTexture r2)
            {
                if (HmdOn)
                {
                    EVRCompositorError vrCompositorError = EVRCompositorError.None;
                    if (left && !leftReady)
                    {
                        //       log("left");
                        lock (KerbalVRPlugin.hmdRightEyeRenderTexture) lock (KerbalVRPlugin.hmdLeftEyeRenderTexture) lock (r) lock (vrCompositor)
                                    {
                                        //             log("leftLOCK");
                                        //         log("left");
                                        hmdLeftEyeTexture.handle = r.GetNativeTexturePtr();

                                        vrCompositorError = vrCompositor.Submit(EVREye.Eye_Left, ref hmdLeftEyeTexture, ref hmdTextureBounds, EVRSubmitFlags.Submit_Default);
                                        leftReady = true;
                                        if (vrCompositorError != EVRCompositorError.None && vrCompositorError != lastError)
                                        {
                                            lastError = vrCompositorError;

                                            warn("Submit (Eye_Left) failed: " + vrCompositorError.ToString());
                                        }
                                    }
                    }
                    else if (!rightReady)
                    {
                        //     log("right");
                        lock (KerbalVRPlugin.hmdRightEyeRenderTexture) lock (KerbalVRPlugin.hmdLeftEyeRenderTexture) lock (r) lock (vrCompositor)
                                    {
                                        //              log("rightLOCK");
                                        hmdRightEyeTexture.handle = r.GetNativeTexturePtr();

                                        vrCompositorError = vrCompositor.Submit(EVREye.Eye_Right, ref hmdRightEyeTexture, ref hmdTextureBounds, EVRSubmitFlags.Submit_Default);
                                        rightReady = true;
                                        if (vrCompositorError != EVRCompositorError.None && vrCompositorError != lastError)
                                        {
                                            lastError = vrCompositorError;

                                            warn("Submit (Eye_Left) failed: " + vrCompositorError.ToString());
                                        }
                                        posTracker.gotPoses = false;
                                    }
                    }
                }
            }
        }

        private class posTracker : MonoBehaviour
        {
            public static bool HmdOn = false;
            public static bool gotPoses = false;

            private TrackedDevicePose_t[] vrDevicePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
            private TrackedDevicePose_t[] vrRenderPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
            private TrackedDevicePose_t[] vrGamePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

            private uint ctrlIndexLeft = 0;
            private uint ctrlIndexRight = 0;

            private static Utils.RigidTransform hmdTransform;
            private static Utils.RigidTransform hmdLeftEyeTransform;
            private static Utils.RigidTransform hmdRightEyeTransform;
            private static Utils.RigidTransform ctrlPoseLeft;
            private static Utils.RigidTransform ctrlPoseRight;


            private static Utils.RigidTransform lastTransform;
            void OnPreRender()
            {
                //   log("pre");
                if (!gotPoses && HmdOn)
                {
                    //   log("get");
                    lock (KerbalVRPlugin.hmdRightEyeRenderTexture) lock (KerbalVRPlugin.hmdLeftEyeRenderTexture)
                        {
                            //   log("getLOCK");
                            gotPoses = true;

                            EVRCompositorError vrCompositorError = EVRCompositorError.None;

                            vrSystem.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseSeated, predict, vrDevicePoses);
                            HmdMatrix34_t vrLeftEyeTransform = vrSystem.GetEyeToHeadTransform(EVREye.Eye_Left);
                            HmdMatrix34_t vrRightEyeTransform = vrSystem.GetEyeToHeadTransform(EVREye.Eye_Right);
                            vrCompositorError = vrCompositor.WaitGetPoses(vrRenderPoses, vrGamePoses);
                            RenderSlave.leftReady = false;
                            RenderSlave.rightReady = false;
                            if (vrCompositorError != EVRCompositorError.None)
                            {
                                KerbalVRPlugin.warn("WaitGetPoses failed: " + vrCompositorError.ToString());
                            }

                            // convert SteamVR poses to Unity coordinates

                            hmdTransform = new Utils.RigidTransform(vrDevicePoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking);
                            hmdLeftEyeTransform = new Utils.RigidTransform(vrLeftEyeTransform);
                            hmdRightEyeTransform = new Utils.RigidTransform(vrRightEyeTransform);
                            ctrlPoseLeft = new Utils.RigidTransform(vrDevicePoses[ctrlIndexLeft].mDeviceToAbsoluteTracking);
                            ctrlPoseRight = new Utils.RigidTransform(vrDevicePoses[ctrlIndexRight].mDeviceToAbsoluteTracking);
                            //     log(hmdTransform.pos.ToString());

                            // foreach (Camera cam in leftCameras)
                            {
                                hmdTransform.rot = (O_Interior.transform.rotation) * hmdTransform.rot;
                                hmdTransform.pos = (O_Interior.transform.rotation) * hmdTransform.pos + O_Interior.transform.position;

                                camLeft_Interior.transform.localRotation = hmdTransform.rot;
                                camLeft_Interior.transform.localPosition = new Vector3(0f, 0f, 0f);
                                camLeft_Interior.transform.Translate(hmdLeftEyeTransform.pos);
                                camLeft_Interior.transform.localPosition += hmdTransform.pos;
                                                              
                                camRight_Interior.transform.localRotation = hmdTransform.rot;
                                camRight_Interior.transform.localPosition = new Vector3(0f, 0f, 0f);
                                camRight_Interior.transform.Translate(hmdRightEyeTransform.pos);
                                camRight_Interior.transform.localPosition += hmdTransform.pos;

                                //

                                //right cam
                                camLeft_Near.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                                //camRight.transform.RotateAround(new Vector3(0, 0, 0), new Vector3(1, 0, 0), -90);

                                // translate the camera to match the position of the left eye, from origin
                                camLeft_Near.transform.localPosition = new Vector3(0f, 0f, 0f);
                                camLeft_Near.transform.Translate(hmdLeftEyeTransform.pos);

                                // translate the camera to match the position of the HMD
                                camLeft_Near.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos);

                                //right cam
                                camRight_Near.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                                //camRight.transform.RotateAround(new Vector3(0, 0, 0), new Vector3(1, 0, 0), -90);

                                // translate the camera to match the position of the left eye, from origin
                                camRight_Near.transform.localPosition = new Vector3(0f, 0f, 0f);
                                camRight_Near.transform.Translate(hmdRightEyeTransform.pos);

                                // translate the camera to match the position of the HMD
                                camRight_Near.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos);

                                camLeft_Far.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                                //camRight.transform.RotateAround(new Vector3(0, 0, 0), new Vector3(1, 0, 0), -90);

                                // translate the camera to match the position of the left eye, from origin
                                camLeft_Far.transform.localPosition = new Vector3(0f, 0f, 0f);
                                camLeft_Far.transform.Translate(hmdLeftEyeTransform.pos);

                                // translate the camera to match the position of the HMD
                                camLeft_Far.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos);
                                camRight_Far.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                                //camRight.transform.RotateAround(new Vector3(0, 0, 0), new Vector3(1, 0, 0), -90);

                                // translate the camera to match the position of the left eye, from origin
                                camRight_Far.transform.localPosition = new Vector3(0f, 0f, 0f);
                                camRight_Far.transform.Translate(hmdRightEyeTransform.pos);

                                // translate the camera to match the position of the HMD
                                camRight_Far.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos);


                                leftSky.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                                leftSky.transform.localPosition = new Vector3(0f, 0f, 0f);
                                leftSky.transform.Translate(hmdLeftEyeTransform.pos * ScaledSpace.InverseScaleFactor);
                                leftSky.transform.localPosition += (hmdTransform.pos * ScaledSpace.InverseScaleFactor);

                                rightSky.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                                rightSky.transform.localPosition = new Vector3(0f, 0f, 0f);
                                rightSky.transform.Translate(hmdRightEyeTransform.pos * ScaledSpace.InverseScaleFactor);
                                rightSky.transform.localPosition += (hmdTransform.pos * ScaledSpace.InverseScaleFactor);

                                leftStars.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                                leftStars.transform.localPosition = new Vector3(0f, 0f, 0f);
                                leftStars.transform.Translate(hmdLeftEyeTransform.pos* ScaledSpace.InverseScaleFactor);
                                leftStars.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos* ScaledSpace.InverseScaleFactor);

                                rightStars.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                                rightStars.transform.localPosition = new Vector3(0f, 0f, 0f);
                                rightStars.transform.Translate(hmdRightEyeTransform.pos* ScaledSpace.InverseScaleFactor);
                                rightStars.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos* ScaledSpace.InverseScaleFactor);
                                
                                //GalaxyCubeControl.Instance.transform.rotation = hmdTransform

                                //  sky.transform.position = ScaledSpace.Instance.transform.position;
                                //  sky.transform.rotation = hmdTransform.rot;
                                //  sky.farClipPlane = 3.0e7f;
                                //sky.cullingMask = (1 << 10) | (1 << 23);

                                /*  foreach(var go in interiorModelList)
                                  {
                                      // = activeVessel.vesselTransform;// .rootPart.transform.rotation;
                                  }*/


                                lastTransform = hmdTransform;
                            }
                        }
                }
            }
        }

        /// <summary>
        /// Overrides the Start method for a MonoBehaviour plugin.
        /// </summary>
        void Start()
        {

            log("KerbalVrPlugin started.");
            DontDestroyOnLoad(this);
            log("dont destroy!");

            // define what cameras to render to HMD
            cameraNamesToRender = new List<string>();
            cameraNamesToRender.Add(cameraNames[0]); // renders the galaxy
            log("cameras 0 added");
            cameraNamesToRender.Add(cameraNames[1]); // renders space/planets?
            log("cameras 1 added");
            cameraNamesToRender.Add(cameraNames[2]); // renders things far away (like out to the horizon)
            log("cameras 2 added");
            cameraNamesToRender.Add(cameraNames[3]); // renders things close to you
            log("cameras 3 added");
            cameraNamesToRender.Add(cameraNames[4]); // renders the IVA view (cockpit)
            log("cameras 4 added");
            //cameraNamesToRender.Add(cameraNames[5]); // don't render UI, it looks shitty
            //cameraNamesToRender.Add(cameraNames[6]); // don't render UI, it looks shitty

            camerasToRender = new List<CameraProperties>(cameraNamesToRender.Count);

            interiorModelList = new List<GameObject>();


        }


        bool HmdOn = false;
        bool master = true;

        private WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();

        /*void LateUpdate()
        {
            if (HmdOn)
            {
                foreach (int id in leftCameras)
                {

                    //Left camera position:##########################################################
                    Camera.allCameras[id].transform.localRotation = hmdTransform.rot;

                    // translate the camera to match the position of the left eye, from origin
                    Camera.allCameras[id].transform.localPosition = new Vector3(0f, 0f, 0f);
                    Camera.allCameras[id].transform.Translate(hmdLeftEyeTransform.pos);

                    // translate the camera to match the position of the HMD
                    Camera.allCameras[id].transform.localPosition += hmdTransform.pos;

                }
            }
        }*/





        void Update()
        {
            if (Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                predict += 0.01f;
                log("predict interval set to: " + predict);
            }
            if (Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                predict -= 0.01f;
                log("predict interval set to: " + predict);
            }

            /*     if (Input.GetKeyDown(KeyCode.Keypad5))
                 {
                     log("cameras:");
                     foreach (Camera c in Camera.allCameras)
                     {
                         log("    " + c.name);
                     }
                 }*/

            if (Input.GetKeyDown(KeyCode.Keypad0) && HmdOn)
            {
                vrSystem.ResetSeatedZeroPose();
                log("Seated pose reset!");
            }

            if (Input.GetKeyDown(KeyCode.KeypadMultiply))
            {
                camLeft_Near.cullingMask = camLeft_Near.cullingMask << 1;
                camRight_Near.cullingMask = camLeft_Near.cullingMask;
                log(Convert.ToString(camLeft_Near.cullingMask, 2));
                if (camLeft_Near.cullingMask == 0)
                {
                    camLeft_Near.cullingMask = 1;
                    camRight_Near.cullingMask = 1;
                }
            }


            if (Input.GetKeyDown(KeyCode.KeypadDivide))
            {
                camLeft_Near.cullingMask = camLeft_Near.cullingMask >> 1;
                camRight_Near.cullingMask = camLeft_Near.cullingMask;
                log(Convert.ToString(camLeft_Near.cullingMask, 2));
                if (camLeft_Near.cullingMask == 0)
                {
                    camLeft_Near.cullingMask = 1;
                    camRight_Near.cullingMask = 1;
                }
            }
            if (Input.GetKeyDown(KeyCode.Keypad2))
            {
                log("          ScaledSpace rot = " + ScaledSpace.Instance.transform.rotation.eulerAngles.ToString());
                log("    GalaxyCubeControl rot = " + GalaxyCubeControl.Instance.transform.rotation.eulerAngles.ToString());
                log("GalaxyCubeControl tgt rot = " + GalaxyCubeControl.Instance.tgt.transform.rotation.eulerAngles.ToString());
                log("  GalaxyCameraControl rot = " + GalaxyCameraControl.Instance.transform.rotation.eulerAngles.ToString());
                log("         ScaledCamera rot = " + ScaledCamera.Instance.transform.rotation.eulerAngles.ToString());

            }

            if (HmdOn)
            {




                if (Input.GetKeyDown(KeyCode.Keypad1))
                {
                    log("L:" + Convert.ToString(camLeft_Near.cullingMask, 2));
                    log("R:" + Convert.ToString(camRight_Near.cullingMask, 2));
                }


                int tmp = 0;
                if (Input.GetKey(KeyCode.LeftShift))
                {
                    tmp = 10;
                }
                else if (Input.GetKey(KeyCode.RightShift))
                {
                    tmp = 20;
                }
                else if (Input.GetKey(KeyCode.RightControl))
                {
                    tmp = 30;
                }

                if (Input.GetKeyDown(KeyCode.Alpha0))
                {
                    camLeft_Near.cullingMask ^= (1 << 0 + tmp);
                }
                if (Input.GetKeyDown(KeyCode.Alpha1))
                {
                    camLeft_Near.cullingMask ^= (1 << 1 + tmp);
                }
                if (Input.GetKeyDown(KeyCode.Alpha2))
                {
                    camLeft_Near.cullingMask ^= (1 << 2 + tmp);
                }
                if (Input.GetKeyDown(KeyCode.Alpha3))
                {
                    camLeft_Near.cullingMask ^= (1 << 3 + tmp);
                }
                if (Input.GetKeyDown(KeyCode.Alpha4))
                {
                    camLeft_Near.cullingMask ^= (1 << 4 + tmp);
                }
                if (Input.GetKeyDown(KeyCode.Alpha5))
                {
                    camLeft_Near.cullingMask ^= (1 << 5 + tmp);
                }
                if (Input.GetKeyDown(KeyCode.Alpha6))
                {
                    camLeft_Near.cullingMask ^= (1 << 6 + tmp);
                }
                if (Input.GetKeyDown(KeyCode.Alpha7))
                {
                    camLeft_Near.cullingMask ^= (1 << 7 + tmp);
                }
                if (Input.GetKeyDown(KeyCode.Alpha8))
                {
                    camLeft_Near.cullingMask ^= (1 << 8 + tmp);
                }
                if (Input.GetKeyDown(KeyCode.Alpha9))
                {
                    camLeft_Near.cullingMask ^= (1 << 9 + tmp);
                }
                camRight_Near.cullingMask = camLeft_Near.cullingMask;

                if (Input.GetKeyDown(KeyCode.Keypad7))
                {
                    if (camRight_Near.transparencySortMode == TransparencySortMode.Default)
                    {
                        camRight_Near.transparencySortMode = TransparencySortMode.Orthographic;
                        camLeft_Near.transparencySortMode = TransparencySortMode.Orthographic;
                    }
                    if (camRight_Near.transparencySortMode == TransparencySortMode.Orthographic)
                    {
                        camRight_Near.transparencySortMode = TransparencySortMode.Perspective;
                        camLeft_Near.transparencySortMode = TransparencySortMode.Perspective;
                    }
                    if (camRight_Near.transparencySortMode == TransparencySortMode.Perspective)
                    {
                        camRight_Near.transparencySortMode = TransparencySortMode.Default;
                        camLeft_Near.transparencySortMode = TransparencySortMode.Default;
                    }
                }
                if (Input.GetKeyDown(KeyCode.Keypad8))
                {
                    if (camRight_Near.opaqueSortMode == UnityEngine.Rendering.OpaqueSortMode.Default)
                    {
                        camRight_Near.opaqueSortMode = UnityEngine.Rendering.OpaqueSortMode.FrontToBack;
                        camLeft_Near.opaqueSortMode = UnityEngine.Rendering.OpaqueSortMode.FrontToBack;
                    }
                    if (camRight_Near.opaqueSortMode == UnityEngine.Rendering.OpaqueSortMode.FrontToBack)
                    {
                        camRight_Near.opaqueSortMode = UnityEngine.Rendering.OpaqueSortMode.NoDistanceSort;
                        camLeft_Near.opaqueSortMode = UnityEngine.Rendering.OpaqueSortMode.NoDistanceSort;
                    }
                    if (camRight_Near.opaqueSortMode == UnityEngine.Rendering.OpaqueSortMode.NoDistanceSort)
                    {
                        camRight_Near.opaqueSortMode = UnityEngine.Rendering.OpaqueSortMode.Default;
                        camLeft_Near.opaqueSortMode = UnityEngine.Rendering.OpaqueSortMode.Default;
                    }
                }

            }

            if (Input.GetKeyDown(KeyCode.Keypad0) && !HmdOn)
            {
                activeVessel = FlightGlobals.ActiveVessel;

                var goArray = FindObjectsOfType<GameObject>();

                /*GameObject tmp = new GameObject();
                tmp.transform.position = activeVessel.transform.position;
                tmp.transform.SetParent(activeVessel.transform);
                for (var i = 0; i < goArray.Length; i++)
                {
                    if (goArray[i].layer == 16 | goArray[i].layer == 20)
                    {
                        log(goArray[i].name);
                        //Vector3 tmp = goArray[i].transform.position;
                        

                        goArray[i].transform.SetParent(tmp.transform);
                        interiorModelList.Add(goArray[i]);
                    }
                }
                tmp.transform.rotation = activeVessel.transform.rotation;
                */
                HmdOn = true;
                log("TEST!!!!!!!!!!!!!!!!!!");
                log(ScaledSpace.Instance.transform.rotation.eulerAngles.ToString());
                //setup OpenVR
                setup();


                int mask = 0;
                Camera pit = Camera.main;


                uint renderTextureWidth = 0;
                uint renderTextureHeight = 0;
                vrSystem.GetRecommendedRenderTargetSize(ref renderTextureWidth, ref renderTextureHeight);

                HmdMatrix34_t vrLeftEyeTransform = vrSystem.GetEyeToHeadTransform(EVREye.Eye_Left);
                HmdMatrix34_t vrRightEyeTransform = vrSystem.GetEyeToHeadTransform(EVREye.Eye_Right);



                eyeDiference = (int)(Camera.main.WorldToScreenPoint(new Utils.RigidTransform(vrLeftEyeTransform).pos) - Camera.main.WorldToScreenPoint(new Utils.RigidTransform(vrRightEyeTransform).pos)).magnitude;
                eyeDiference = 0;

                skyTexture = new RenderTexture((int)(renderTextureWidth + eyeDiference), (int)renderTextureHeight, 24, RenderTextureFormat.ARGB32);
                skyTexture.Create();

                hmdLeftEyeRenderTexture = new RenderTexture((int)renderTextureWidth, (int)renderTextureHeight, 24, RenderTextureFormat.ARGB32);
                hmdLeftEyeRenderTexture.Create();

                hmdRightEyeRenderTexture = new RenderTexture((int)renderTextureWidth, (int)renderTextureHeight, 24, RenderTextureFormat.ARGB32);
                hmdRightEyeRenderTexture.Create();

                float max = 0;

                float[] distances = new float[32];

                foreach (string cameraName in cameraNamesToRender)
                {
                    foreach (Camera camera in Camera.allCameras)
                    {
                        if (cameraName.Equals(camera.name))
                        {
                            switch (camera.name)
                            {
                                case "GalaxyCamera":
                                    O_Galaxy = camera;
                                    break;
                                case "Camera ScaledSpace":
                                    O_SclaledSpace = camera;
                                    break;
                                case "Camera 01":
                                    O_Far = camera;
                                    break;
                                case "Camera 00":
                                    O_Near = camera;
                                    break;
                                default:
                                    break;
                            }


                            mask |= camera.cullingMask;

                            max = Math.Max(camera.farClipPlane, max);
                            string bitMask = Convert.ToString(camera.cullingMask, 2);
                            for (int i = 0; i < distances.Length; i++)
                            {
                                distances[i] = Math.Max(distances[i], Math.Max(camera.layerCullDistances[i], camera.farClipPlane));
                            }

                            camera.gameObject.AddOrGetComponent<posTracker>();

                            log("Camera:");
                            log("  Name:  " + camera.name);
                            log("  mask:  " + Convert.ToString(camera.cullingMask, 2));
                            log("  depth: " + camera.depth);
                            log("");

                            if (cameraName.Equals("InternalCamera"))
                            {
                                pit = camera;
                                camLeft_Near = camera;
                                //camRight = camera;
                                leftCameras.Add(camera);
                                O_Interior = camera;
                            }
                            if (cameraName.Equals("GalaxyCamera"))
                            {
                                O_Galaxy = camera;
                            }

                            if (cameraName.Equals("Camera ScaledSpace"))
                            {
                                //  sky = camera;
                                O_SclaledSpace = camera;
                                log("sky cam rot = " + camera.transform.rotation.eulerAngles.ToString());
                            }
                        }
                    }
                }

                camRight_Interior = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);
                camLeft_Interior = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);

                camRight_Near = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);
                camLeft_Near = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);

                camRight_Far = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);
                camLeft_Far = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);

                leftSky = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);
                rightSky = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);

                leftStars = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);
                rightStars = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);

                leftSky.CopyFrom(O_SclaledSpace);
                rightSky.CopyFrom(O_SclaledSpace);
                leftStars.CopyFrom(O_Galaxy);
                rightStars.CopyFrom(O_Galaxy);
                camRight_Near.CopyFrom(O_Near);
                camLeft_Near.CopyFrom(O_Near);
                camLeft_Interior.CopyFrom(O_Interior);
                camRight_Interior.CopyFrom(O_Interior);
                camRight_Far.CopyFrom(O_Far);
                camLeft_Far.CopyFrom(O_Far);

                //leftSky.transform.SetParent(activeVessel.transform);
                //rightSky.transform.SetParent(activeVessel.transform);
                //leftStars.transform.SetParent(activeVessel.transform);
                //rightStars.transform.SetParent(activeVessel.transform);
                //camRight_Near.transform.SetParent(activeVessel.transform);
                //camLeft_Near.transform.SetParent(activeVessel.transform);
                //camLeft_Interior.transform.SetParent(activeVessel.transform);
                //camRight_Interior.transform.SetParent(activeVessel.transform);
                //camRight_Far.transform.SetParent(activeVessel.transform);
                //camLeft_Far.transform.SetParent(activeVessel.transform);


                // stars.clearFlags = CameraClearFlags.Depth;
                // stars.cullingMask = (1 << 18);
                //sky.cullingMask = (1 << 9);
                leftStars.targetTexture = hmdLeftEyeRenderTexture;
                rightStars.targetTexture = hmdRightEyeRenderTexture;
                //
                leftSky.targetTexture = hmdLeftEyeRenderTexture;
                rightSky.targetTexture = hmdRightEyeRenderTexture;

                //skyCopySlave = rightSky.gameObject.AddComponent<copySlave>();
                // skyCopySlave.leftTarget = hmdLeftEyeRenderTexture;
                // skyCopySlave.rightTarget = hmdRightEyeRenderTexture;
                //skyCopySlave.difrence = widthDiference;

                camLeft_Near.targetTexture = hmdLeftEyeRenderTexture;
                camRight_Near.targetTexture = hmdRightEyeRenderTexture;

                camLeft_Interior.targetTexture = hmdLeftEyeRenderTexture;
                camRight_Interior.targetTexture = hmdRightEyeRenderTexture;


                camLeft_Far.targetTexture = hmdLeftEyeRenderTexture;
                camRight_Far.targetTexture = hmdRightEyeRenderTexture;

               // leftStars.depth += 4;
               // leftSky.depth += 4;
               // camLeft_Near.depth += 4;
               // camLeft_Far.depth += 4;
               // camLeft_Interior.depth += 4;
               // camRight_Interior.depth += 4;
                //rightSky.depth = -5;
                //  camRight.targetTexture = sky.targetTexture;
                //  camLeft.targetTexture = sky.targetTexture;

                //   camera.targetTexture = hmdLeftEyeRenderTexture;
                //     camRight.cullingMask = (1 << 0) | (1 << 4) | (1 << 10) | (1 << 15) | (1 << 16) | (1 << 20) | (1 << 23);
                //    camLeft.cullingMask = (1 << 0) | (1 << 4) | (1 << 10) | (1 << 15) | (1 << 16) | (1 << 20) | (1 << 23);

                // 0: ship exterior
                //15: ground
                //16: ship interior

                //TODO change to |=
                //camRight_Near.cullingMask = 1 << 20 | (1 << 16);
                //camLeft_Near.cullingMask = 1 << 20 | (1 << 16); 



                //  camLeft.clearFlags = CameraClearFlags.Depth;
                //  camRight.clearFlags = CameraClearFlags.Depth;

                // camLeft_Near.layerCullDistances = distances;
                // camRight_Near.layerCullDistances = distances;


                //  camLeft.depthTextureMode = DepthTextureMode.Depth;
                //  camRight.depthTextureMode = DepthTextureMode.Depth;
                //camRight.transparencySortMode = TransparencySortMode.Perspective;
                //create left slave
                leftSlave = camLeft_Interior.gameObject.AddOrGetComponent<RenderSlave>();
                //leftSlave = sky.gameObject.AddOrGetComponent<RenderSlave>();
                leftSlave.left = true;
                // camLeft.cullingMask = (1 << 0) | (1 << 4) | (1 << 9) | (1 << 10) | (1 << 15) | (1 << 16) | (1 << 18) | (1 << 20) | (1 << 23);
                //camLeft.cullingMask = (1 << 9) | (1 << 15) | (1 << 16) | (1 << 20) | (1 << 32);
                //camLeft.cullingMask = 0;
                //  camLeft.ResetCullingMatrix();
                //camLeft_Near.useOcclusionCulling = true;
                //camLeft_Near.nearClipPlane = 0.01f;
                //camLeft_Near.farClipPlane = max;

                //create right slave
                rightSlave = camRight_Interior.gameObject.AddOrGetComponent<RenderSlave>();
                rightSlave.left = false;
                // camRight.cullingMask = (1 << 0) | (1 << 4) | (1 << 9) | (1 << 10) | (1 << 15) | (1 << 16) | (1 << 18) | (1 << 20) | (1 << 23);
                //   camRight.cullingMask = (1 << 9) | (1 << 15) | (1 << 16) | (1 << 20) | (1 << 32);
                //  camRight.cullingMask = 0;
                //camRight.ResetCullingMatrix();
                // camRight_Near.useOcclusionCulling = true;
                //
                // camRight_Near.nearClipPlane = 0.01f;
                // camRight_Near.farClipPlane = max;
                //set camera projections:


                //TODO rewrite
                HmdMatrix44_t projLeft = vrSystem.GetProjectionMatrix(EVREye.Eye_Left, camLeft_Near.nearClipPlane, camLeft_Near.farClipPlane);
                HmdMatrix44_t projRight = vrSystem.GetProjectionMatrix(EVREye.Eye_Right, camRight_Near.nearClipPlane, camRight_Near.farClipPlane);

                camLeft_Near.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft);
                camRight_Near.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projRight);

                HmdMatrix44_t projLeft2 = vrSystem.GetProjectionMatrix(EVREye.Eye_Left, camLeft_Interior.nearClipPlane, camLeft_Interior.farClipPlane);
                HmdMatrix44_t projRight2 = vrSystem.GetProjectionMatrix(EVREye.Eye_Right, camRight_Interior.nearClipPlane, camRight_Interior.farClipPlane);
               //
                camLeft_Interior.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft2);
                camRight_Interior.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projRight2);

                HmdMatrix44_t projLeft3 = vrSystem.GetProjectionMatrix(EVREye.Eye_Left, camLeft_Far.nearClipPlane, camLeft_Far.farClipPlane);
                HmdMatrix44_t projRight3 = vrSystem.GetProjectionMatrix(EVREye.Eye_Right, camRight_Far.nearClipPlane, camRight_Far.farClipPlane);

                camLeft_Far.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft3);
                camRight_Far.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projRight3);

                HmdMatrix44_t projLeft4 = vrSystem.GetProjectionMatrix(EVREye.Eye_Left, leftSky.nearClipPlane, leftSky.farClipPlane);
                HmdMatrix44_t projRight4 = vrSystem.GetProjectionMatrix(EVREye.Eye_Right, rightSky.nearClipPlane, rightSky.farClipPlane);

                leftSky.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft4);
                rightSky.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projRight4);

                HmdMatrix44_t projLeft5 = vrSystem.GetProjectionMatrix(EVREye.Eye_Left, leftStars.nearClipPlane, leftStars.farClipPlane);
                HmdMatrix44_t projRight5 = vrSystem.GetProjectionMatrix(EVREye.Eye_Right, rightStars.nearClipPlane, rightStars.farClipPlane);

                leftStars.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft5);
                rightStars.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projRight5);

                //camLeft_Interior.depth = 0;
                //camLeft_Interior.clearFlags = CameraClearFlags.Skybox;
                camLeft_Near.depth = 0;
                // camLeft_Near.clearFlags = CameraClearFlags.Depth;
                camLeft_Far.depth = -1;
                // camLeft_Far.clearFlags = CameraClearFlags.Depth;
                leftSky.depth = 4;
                // leftSky.clearFlags = CameraClearFlags.Depth;
                leftStars.depth = 3;
                // leftStars.clearFlags = CameraClearFlags.Depth;
                //
                //camRight_Interior.depth = 0-5;
                //camRight_Interior.clearFlags = CameraClearFlags.Skybox;
                camRight_Near.depth = 0 - 5;
                //  camRight_Near.clearFlags = CameraClearFlags.Depth;
                camRight_Far.depth = -1 - 5;
                //  camRight_Far.clearFlags = CameraClearFlags.Depth;
                rightSky.depth = 4 - 5;
                //  rightSky.clearFlags = CameraClearFlags.Depth;
                rightStars.depth = 3 - 5;
                //   rightStars.clearFlags = CameraClearFlags.Depth;


                O_SclaledSpace.enabled = false;
                O_Galaxy.enabled = false;
                O_Near.enabled = false;
                O_Far.enabled = false;
                O_Interior.enabled = false;

                camLeft_Near.enabled = false;
                camRight_Near.enabled = false;

                camLeft_Far.enabled = false;
                camRight_Far.enabled = false;

                leftSky.enabled = false;
                rightSky.enabled = false;

                leftStars.enabled = false;
                rightStars.enabled = false;

                camLeft_Interior.enabled = false;
                camRight_Interior.enabled = false;


                
                //activate slaves
                posTracker.HmdOn = true;
                leftSlave.HmdOn = true;
                rightSlave.HmdOn = true;

                
            }
            if (HmdOn)
            {
                leftStars.Render();
                rightStars.Render();

                leftSky.Render();
                rightSky.Render();

              //  Graphics.CopyTexture(hmdRightEyeRenderTexture, hmdLeftEyeRenderTexture);

                int xstart = 0;
                int ystart = 0;
                int width = hmdLeftEyeRenderTexture.width;
                int height = hmdLeftEyeRenderTexture.height;
                int destX = 0;
                int destY = 0;
               



                camLeft_Far.Render();
                camRight_Far.Render();

               // Graphics.CopyTexture(skyTexture, 0, 0, xstart + eyeDiference, ystart, width, height, hmdLeftEyeRenderTexture, 0, 0, destX, destY);
               // Graphics.CopyTexture(skyTexture, 0, 0, xstart, ystart, width, height, hmdRightEyeRenderTexture, 0, 0, destX, destY);

                camLeft_Near.Render();
                camRight_Near.Render();

                camLeft_Interior.Render();
                camRight_Interior.Render();
            }
        }

        /*void OnDestroy()
        {
            log("KerbalVrPlugin OnDestroy");
            posTracker.HmdOn = false;
            rightSlave.HmdOn = false;
            leftSlave.HmdOn = false;
            vrSystem.ReleaseInputFocus();
            OpenVR.Shutdown();
            HmdOn = false;

        }*/

        public void setup()
        {


            var error = EVRInitError.None;
            vrSystem = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Scene);

            if ((int)error != (int)EVRInitError.None)
            {
                log("KerbalVrPlugin started.");
            }
            else
            {
                err(error.ToString());
            }

            //rendervalues #########################################################
            // Setup render values
            uint w = 0, h = 0;
            vrSystem.GetRecommendedRenderTargetSize(ref w, ref h);
            float sceneWidth = (float)w;
            float sceneHeight = (float)h;


            float l_left = 0.0f, l_right = 0.0f, l_top = 0.0f, l_bottom = 0.0f;
            vrSystem.GetProjectionRaw(EVREye.Eye_Left, ref l_left, ref l_right, ref l_top, ref l_bottom);

            float r_left = 0.0f, r_right = 0.0f, r_top = 0.0f, r_bottom = 0.0f;
            vrSystem.GetProjectionRaw(EVREye.Eye_Right, ref r_left, ref r_right, ref r_top, ref r_bottom);

            Vector2 tanHalfFov = new Vector2(Mathf.Max(-l_left, l_right, -r_left, r_right), Mathf.Max(-l_top, l_bottom, -r_top, r_bottom));


            hmdLeftEyeTexture = new Texture_t();
            hmdLeftEyeTexture.eColorSpace = EColorSpace.Auto;

            hmdRightEyeTexture = new Texture_t();
            hmdRightEyeTexture.eColorSpace = EColorSpace.Auto;

            switch (SystemInfo.graphicsDeviceType)
            {
                case UnityEngine.Rendering.GraphicsDeviceType.OpenGL2:
                    log("OpenGL2");
                    hmdLeftEyeTexture.eType = ETextureType.OpenGL;
                    hmdRightEyeTexture.eType = ETextureType.OpenGL;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore:
                    log("OpenCore");
                    hmdLeftEyeTexture.eType = ETextureType.OpenGL;
                    hmdRightEyeTexture.eType = ETextureType.OpenGL;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2:
                    log("OpenGLES2");
                    hmdLeftEyeTexture.eType = ETextureType.OpenGL;
                    hmdRightEyeTexture.eType = ETextureType.OpenGL;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3:
                    log("OpenGLES3");
                    hmdLeftEyeTexture.eType = ETextureType.OpenGL;
                    hmdRightEyeTexture.eType = ETextureType.OpenGL;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.Direct3D9:
                    log("Direct3D9");
                    warn("DirectX 9 mode not Supported! There be Dragons!");
                    hmdLeftEyeTexture.eType = ETextureType.DirectX;
                    hmdRightEyeTexture.eType = ETextureType.DirectX;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.Direct3D11:
                    log("Direct3D11");
                    hmdLeftEyeTexture.eType = ETextureType.DirectX;
                    hmdRightEyeTexture.eType = ETextureType.DirectX;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.Direct3D12:
                    log("Direct3D12");
                    warn("DirectX 12 mode not implemented! There be Dragons!");
                    hmdLeftEyeTexture.eType = ETextureType.DirectX12;
                    hmdRightEyeTexture.eType = ETextureType.DirectX12;
                    break;
                default:
                    throw (new Exception(SystemInfo.graphicsDeviceType.ToString() + " not supported"));
            }



            vrCompositor = OpenVR.Compositor;

            if (!vrCompositor.CanRenderScene())
            {
                err("can not render scene");
            }
        }



        public static void log(string msg)
        {

            Debug.Log("[KerbalVR] " + msg);
        }

        public static void warn(string msg)
        {
            Debug.LogWarning("[KerbalVR] " + msg);
        }

        public static void err(string msg)
        {
            Debug.LogError("[KerbalVR] " + msg);
        }
    }

}
