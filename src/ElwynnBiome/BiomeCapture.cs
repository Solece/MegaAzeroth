using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// Batch-only native rendering; restores every temporary camera/Canvas setting.
// Feature assertions live in the biome probe, independently of image capture.
static class BiomeCapture
{
    internal static void Capture(string fileName)
    {
        if (!Plugin.Smoke) throw new InvalidOperationException("Biome capture requires the batch probe");
        MyPlayerMinimap();
        var camera=Camera.main;
        if(camera==null)throw new InvalidOperationException("Native battle camera missing");
        var canvases=Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas && c.isActiveAndEnabled).ToArray();
        var states=canvases.Select(c=>(Canvas:c,Mode:c.renderMode,Camera:c.worldCamera,Distance:c.planeDistance)).ToArray();
        var previousTarget=camera.targetTexture;var previousActive=RenderTexture.active;int previousMask=camera.cullingMask;
        // Map markers are 3D meshes on a map-only layer. Including that layer for
        // native Canvas graphics must not render those meshes in the combat view.
        var mapOnlyRenderers=Resources.FindObjectsOfTypeAll<Renderer>().Where(r=>r!=null && r.gameObject.activeInHierarchy && r.enabled && !r.forceRenderingOff && (previousMask&(1<<r.gameObject.layer))==0).ToArray();
        const int width=960,height=540;
        var rt=new RenderTexture(width,height,24);rt.Create();
        Texture2D image=null;
        try
        {
            camera.targetTexture=rt;
            // Native overlay UI bypasses the world camera mask. Include its layers only
            // for this offscreen camera conversion, then restore the real gameplay mask.
            foreach(var graphic in Resources.FindObjectsOfTypeAll<Graphic>())
                if(graphic!=null && graphic.gameObject.activeInHierarchy)camera.cullingMask|=1<<graphic.gameObject.layer;
            foreach(var canvas in canvases)if(canvas.renderMode==RenderMode.ScreenSpaceOverlay){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
            foreach(var marker in mapOnlyRenderers)marker.forceRenderingOff=true;
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
            image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            var path=Path.Combine(Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath, "../reports")),fileName);
            using var stream=File.Create(path);stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
            for(int y=height-1;y>=0;y--)for(int x=0;x<width;x++)
            {
                var pixel=image.GetPixel(x,y);stream.WriteByte((byte)Mathf.Clamp(pixel.r*255,0,255));stream.WriteByte((byte)Mathf.Clamp(pixel.g*255,0,255));stream.WriteByte((byte)Mathf.Clamp(pixel.b*255,0,255));
            }
        }
        finally
        {
            foreach(var state in states){state.Canvas.renderMode=state.Mode;state.Canvas.worldCamera=state.Camera;state.Canvas.planeDistance=state.Distance;}
            foreach(var marker in mapOnlyRenderers)if(marker!=null)marker.forceRenderingOff=false;
            camera.cullingMask=previousMask;camera.targetTexture=previousTarget;RenderTexture.active=previousActive;rt.Release();Object.Destroy(rt);if(image!=null)Object.Destroy(image);
        }
    }
    static void MyPlayerMinimap() => Assets.Scripts.Actors.Player.MyPlayer.Instance?.minimapCamera?.Render();
}
