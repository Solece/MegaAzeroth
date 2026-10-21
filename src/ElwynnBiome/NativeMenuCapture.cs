using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// Capture the actual scene camera, including native post-processing. Do not draw
// the preview again in the UI pass, which would conceal depth-of-field defects.
internal static class NativeMenuCapture
{
    internal static void Capture(string path)
    {
        var menu=Object.FindObjectsOfType<MenuCamera>().FirstOrDefault(m=>m!=null&&m.camera!=null&&m.camera.enabled);
        var camera=menu?.camera??Camera.main;if(camera==null)throw new InvalidOperationException("No native menu camera");
        var canvases=Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas&&c.enabled&&c.renderMode!=RenderMode.WorldSpace).ToArray();
        var previous=camera.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(960,540,24);rt.Create();Texture2D image=null;
        try
        {
            foreach(var canvas in canvases)canvas.enabled=false;
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();
            var pixels=image.GetPixels32();var bytes=new byte[960*540*3];int at=0;
            for(int y=539;y>=0;y--)for(int x=0;x<960;x++){var pixel=pixels[y*960+x];bytes[at++]=pixel.r;bytes[at++]=pixel.g;bytes[at++]=pixel.b;}
            using var stream=File.Create(path);stream.Write(System.Text.Encoding.ASCII.GetBytes("P6\n960 540\n255\n"));stream.Write(bytes);
        }
        finally
        {
            foreach(var canvas in canvases)if(canvas!=null)canvas.enabled=true;
            camera.targetTexture=previous;RenderTexture.active=active;rt.Release();Object.Destroy(rt);if(image!=null)Object.Destroy(image);
        }
    }
}
