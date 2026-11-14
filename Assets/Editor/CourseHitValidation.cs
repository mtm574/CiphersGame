#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CourseHitValidation
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    [MenuItem("Tools/VR Body/Validate Course Hits")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        var report = new StringBuilder();
        Scene scene = default;
        try
        {
            scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BasicScene.unity");
            var roots = scene.GetRootGameObjects();
            var controller = roots.SelectMany(r => r.GetComponentsInChildren<CourseHitController>(true)).Single();
            Require(Field<Camera>(controller, "playerCamera") != null, "VR camera reference resolves");
            var shader = Field<Shader>(controller, "flashShader");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "Flash shader imports without errors");
            Require(LayerMask.NameToLayer("PlayerHitbox") >= 0, "Hitbox layer exists");
            Require(Physics.GetIgnoreLayerCollision(9,9), "Self collisions disabled");
            Invoke(controller, "Awake");
            typeof(CourseHitController).GetProperty("TrackingReady").SetValue(controller, true);
            var proxies = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerBodyHitbox>(true)).ToArray();
            Require(proxies.Length == 14, "All 14 body hitboxes created");
            Require(proxies.All(p => p.GetComponent<Rigidbody>().isKinematic && !p.GetComponent<Rigidbody>().useGravity && p.GetComponent<Collider>().isTrigger), "Hitboxes detect without forces");
            var obstacles = roots.SelectMany(r => r.GetComponentsInChildren<CourseObstacle>(true)).ToArray();
            Require(obstacles.Length == 20, "20 obstacle groups marked");
            var obstacle = obstacles.First(o => o.GetComponentInChildren<Collider>() != null);
            var collider = obstacle.GetComponentInChildren<Collider>();
            var arena = controller.GetComponent<RotatingArena>();
            Invoke(arena,"Start");
            Set(arena,"delayRemaining",0f);
            var jog = Field<Animator>(controller,"avatar").GetComponent<VRJogAnimator>();
            Invoke(jog,"Awake");
            var animator = jog.GetComponent<Animator>();
            animator.SetFloat("JogWeight",1f);
            float timeScale = Time.timeScale;
            controller.Contact(proxies[0], collider, true);
            Require(controller.IsPaused && Math.Abs(controller.PauseRemaining-2f)<0.001f, "Initial hit starts 2-second pause");
            Require(animator.GetFloat("JogWeight")==0f, "Hit immediately rests jogging");
            Require(arena.GetSurfaceVelocity(Vector3.one)==Vector3.zero, "Paused floor reports no velocity");
            controller.Tick(.5f);
            controller.Contact(proxies[1],collider,true);
            Require(Math.Abs(controller.PauseRemaining-1.5f)<.001f, "Multi-limb contact does not extend pause");
            var windmill = controller.GetComponentInChildren<WindmillRotator>();
            var sliding = controller.GetComponentInChildren<SideToSide>();
            Invoke(sliding,"Start");
            Vector3 beforePosition=sliding.transform.localPosition;
            Quaternion beforeRotation=windmill.transform.localRotation;
            float beforePhase=Field<float>(sliding,"motionTime");
            float beforeWindPhase=Field<float>(windmill,"motionTime");
            float delay=Field<float>(arena,"delayRemaining");
            Invoke(sliding,"Update"); Invoke(windmill,"Update"); Invoke(arena,"FixedUpdate");
            Require(sliding.transform.localPosition==beforePosition && Quaternion.Angle(windmill.transform.localRotation,beforeRotation)<.001f, "Movers remain stationary while paused");
            Require(Field<float>(sliding,"motionTime")==beforePhase && Field<float>(windmill,"motionTime")==beforeWindPhase && Field<float>(arena,"delayRemaining")==delay, "Pause preserves phase and startup countdown");
            controller.Tick(1.5f);
            Require(!controller.IsPaused, "Automatic resume after 2 seconds");
            controller.Contact(proxies[0],collider,true);
            Require(!controller.IsPaused, "Still-overlapping obstacle cannot rehit");
            controller.Contact(proxies[0],collider,false); controller.Contact(proxies[1],collider,false);
            controller.Tick(.11f);
            controller.Contact(proxies[0],collider,true);
            Require(controller.IsPaused, "Separated obstacle rearms");
            var flash = controller.GetComponent<VRHitFlash>();
            flash.Flash(); Require(flash.Alpha==1f,"Opaque red at hit");
            flash.Tick(.09f); Require(flash.Alpha==1f,"Opaque hold");
            flash.Tick(.06f); Require(flash.Alpha>0f&&flash.Alpha<1f,"Brief fade");
            flash.Tick(.06f); Require(flash.Alpha==0f,"View fully restores");
            Require(Time.timeScale==timeScale,"Global time and tracking remain unaffected");
            var head = proxies.First(p=>p.Region==PlayerBodyRegion.Head);
            Require(head.From==Field<Camera>(controller,"playerCamera").transform,"Head proxy follows headset independently of head bone scale");
            report.AppendLine("PASS: scene wiring, shader import, 14 kinematic trigger hitboxes, 20 obstacle groups.");
            report.AppendLine("PASS: opaque flash/hold/fade, exact pause, duplicate suppression, overlap rearm, jogging reset, phase/startup preservation, independent head proxy, unchanged global time.");
            // Ensure generated runtime objects are removed before the private scene closes.
            UnityEngine.Object.DestroyImmediate(controller.GetComponent<VRHitFlash>());
            UnityEngine.Object.DestroyImmediate(controller);
            // Original validation checks the unchanged IK against the actual scene avatar.
            var sync = roots.SelectMany(r=>r.GetComponentsInChildren<VRRigSync>(true)).Single();
            VRBodyValidation.RunChecks(sync,report);
        }
        catch(Exception error) { report.AppendLine("FAIL: "+(error.InnerException??error)); throw; }
        finally
        {
            if(scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            Directory.CreateDirectory("Library/CourseHitValidation");
            File.WriteAllText("Library/CourseHitValidation/results.txt",report.ToString());
            Debug.Log(report.ToString());
        }
    }
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Flags).GetValue(o);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
    static object Invoke(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
    static void Require(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }
}
#endif
