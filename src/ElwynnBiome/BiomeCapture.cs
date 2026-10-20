using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// Batch-only transport: world pass keeps native effects, a separate plain camera
// draws screen UI last so menu blur cannot blur normally-overlay text.
static class BiomeCapture
{
    internal static void Capture(string fileName)
    {
        if(!Plugin.Smoke)throw new InvalidOperationException("Biome capture requires an armed batch probe");
        Assets.Scripts.Actors.Player.MyPlayer.Instance?.minimapCamera?.Render();
        var camera=Camera.main;if(camera==null)throw new InvalidOperationException("Native camera missing");
        var screens=Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas&&c.isActiveAndEnabled&&c.renderMode!=RenderMode.WorldSpace).ToArray();
        var states=screens.Select(c=>(Canvas:c,Enabled:c.enabled,Mode:c.renderMode,Camera:c.worldCamera,Distance:c.planeDistance)).ToArray();
        var target=camera.targetTexture;var active=RenderTexture.active;
        var previews=Object.FindObjectsOfType<CharacterDisplay>().Where(d=>d.playerRenderer!=null&&d.gameObject.activeInHierarchy).Select(d=>d.playerRenderer.transform).ToArray();
        var renderers=Object.FindObjectsOfType<Renderer>().Where(r=>r!=null&&r.gameObject.activeInHierarchy).Select(r=>(Renderer:r,Hidden:r.forceRenderingOff)).ToArray();
        const int width=960,height=540;var rt=new RenderTexture(width,height,24);rt.Create();Texture2D image=null;GameObject rig=null;
        try
        {
            foreach(var state in states)state.Canvas.enabled=false;
            camera.targetTexture=rt;camera.Render();
            rig=new GameObject("AzerothBatchOverlayCaptureCamera");var ui=rig.AddComponent<Camera>();ui.enabled=false;ui.CopyFrom(camera);
            ui.transform.position=camera.transform.position;ui.transform.rotation=camera.transform.rotation;
            ui.targetTexture=rt;ui.clearFlags=CameraClearFlags.Depth;ui.cullingMask=-1;
            foreach(var state in states)
            {state.Canvas.enabled=true;state.Canvas.renderMode=RenderMode.ScreenSpaceCamera;state.Canvas.worldCamera=ui;state.Canvas.planeDistance=ui.nearClipPlane+.001f;}
            foreach(var saved in renderers)if(!previews.Any(p=>saved.Renderer.transform==p||saved.Renderer.transform.IsChildOf(p)))saved.Renderer.forceRenderingOff=true;
            Canvas.ForceUpdateCanvases();ui.Render();RenderTexture.active=rt;
            image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            var path=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports",fileName));
            using var stream=File.Create(path);stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
            for(int y=height-1;y>=0;y--)for(int x=0;x<width;x++)
            {var pixel=image.GetPixel(x,y);stream.WriteByte((byte)Mathf.Clamp(pixel.r*255,0,255));stream.WriteByte((byte)Mathf.Clamp(pixel.g*255,0,255));stream.WriteByte((byte)Mathf.Clamp(pixel.b*255,0,255));}
        }
        finally
        {
            foreach(var state in states)
            {state.Canvas.enabled=state.Enabled;state.Canvas.renderMode=state.Mode;state.Canvas.worldCamera=state.Camera;state.Canvas.planeDistance=state.Distance;}
            foreach(var saved in renderers)if(saved.Renderer!=null)saved.Renderer.forceRenderingOff=saved.Hidden;
            camera.targetTexture=target;RenderTexture.active=active;
            if(rig!=null)Object.Destroy(rig);rt.Release();Object.Destroy(rt);if(image!=null)Object.Destroy(image);Canvas.ForceUpdateCanvases();
        }
    }
}
