using UnityEngine;
using UnityEngine.UI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Object=UnityEngine.Object;
namespace AzerothPresentation;

// Explicitly armed QA transport. Native world/postprocessing is rendered once;
// screen UI is composited in its normal order using a separate plain camera.
static class PresentationCapture
{
    internal static byte[] Capture(string path,RectTransform trackedRect=null,Action<int[]> projectedBounds=null)
    {
        if(!PresentationProbe.Armed)throw new InvalidOperationException("Capture requires armed native batch probe");
        var world=Object.FindObjectsOfType<MenuCamera>().FirstOrDefault(c=>c.camera!=null&&c.camera.enabled)?.camera??Camera.main;
        var states=Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas&&c.isActiveAndEnabled&&c.renderMode!=RenderMode.WorldSpace).Select(c=>(Canvas:c,Enabled:c.enabled,Mode:c.renderMode,Camera:c.worldCamera,Distance:c.planeDistance)).ToArray();
        var rendererStates=Object.FindObjectsOfType<Renderer>().Select(r=>(Renderer:r,Hidden:r.forceRenderingOff)).ToArray();
        var terrains=Object.FindObjectsOfType<Terrain>().Select(t=>(Terrain:t,Height:t.drawHeightmap,Trees:t.drawTreesAndFoliage)).ToArray();
        var oldTarget=world!=null?world.targetTexture:null;var active=RenderTexture.active;var rt=new RenderTexture(960,540,24);rt.Create();GameObject rig=null;Texture2D image=null;
        try
        {
            foreach(var s in states)s.Canvas.enabled=false;
            if(world!=null){world.targetTexture=rt;world.Render();}
            rig=new GameObject("AzerothPresentation_ProbeCaptureCamera");var ui=rig.AddComponent<Camera>();ui.enabled=false;
            if(world!=null){ui.CopyFrom(world);ui.transform.position=world.transform.position;ui.transform.rotation=world.transform.rotation;}
            ui.targetTexture=rt;ui.clearFlags=world!=null?CameraClearFlags.Depth:CameraClearFlags.SolidColor;ui.backgroundColor=Color.black;ui.cullingMask=-1;
            foreach(var s in states){s.Canvas.enabled=true;s.Canvas.renderMode=RenderMode.ScreenSpaceCamera;s.Canvas.worldCamera=ui;s.Canvas.planeDistance=ui.nearClipPlane+.001f;}
            foreach(var s in rendererStates)s.Renderer.forceRenderingOff=true;
            foreach(var t in terrains){t.Terrain.drawHeightmap=false;t.Terrain.drawTreesAndFoliage=false;}
            Canvas.ForceUpdateCanvases();
            if(trackedRect!=null&&projectedBounds!=null)
            {
                var corners=new Il2CppStructArray<Vector3>(4);trackedRect.GetWorldCorners(corners);int x0=960,y0=540,x1=0,y1=0;
                foreach(var corner in corners){var p=ui.WorldToViewportPoint(corner);if(p.z<=0)continue;int x=Mathf.Clamp((int)(p.x*960),0,959),y=Mathf.Clamp((int)((1-p.y)*540),0,539);x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);}
                projectedBounds(new[]{x0,y0,x1,y1});
            }
            ui.Render();RenderTexture.active=rt;
            image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();var pixels=image.GetPixels32();var bytes=new byte[960*540*3];int i=0;
            for(int y=539;y>=0;y--)for(int x=0;x<960;x++){var p=pixels[y*960+x];bytes[i++]=p.r;bytes[i++]=p.g;bytes[i++]=p.b;}
            using var file=File.Create(path);file.Write(System.Text.Encoding.ASCII.GetBytes("P6\n960 540\n255\n"));file.Write(bytes);return bytes;
        }
        finally
        {
            foreach(var s in states)try{if(s.Canvas!=null){s.Canvas.enabled=s.Enabled;s.Canvas.renderMode=s.Mode;s.Canvas.worldCamera=s.Camera;s.Canvas.planeDistance=s.Distance;}}catch{}
            foreach(var s in rendererStates)try{if(s.Renderer!=null)s.Renderer.forceRenderingOff=s.Hidden;}catch{}
            foreach(var s in terrains)try{if(s.Terrain!=null){s.Terrain.drawHeightmap=s.Height;s.Terrain.drawTreesAndFoliage=s.Trees;}}catch{}
            if(world!=null)world.targetTexture=oldTarget;RenderTexture.active=active;if(rig!=null)Object.Destroy(rig);rt.Release();Object.Destroy(rt);if(image!=null)Object.Destroy(image);Canvas.ForceUpdateCanvases();
        }
    }
    internal static object HeroPair(string folder,string prefix)
    {
        var hero=Object.FindObjectsOfType<Renderer>().Where(r=>r.name.StartsWith("AzerothMenuHero_",StringComparison.Ordinal)||r.name.StartsWith("AzerothMenuEquipment_",StringComparison.Ordinal)).Select(r=>(Renderer:r,Hidden:r.forceRenderingOff)).ToArray();
        var camera=Object.FindObjectsOfType<MenuCamera>().FirstOrDefault(c=>c.camera!=null&&c.camera.enabled)?.camera??Camera.main;
        int x0=960,y0=540,x1=0,y1=0;
        foreach(var row in hero)
        {
            var b=row.Renderer.bounds;
            for(int i=0;i<8;i++){var p=camera.WorldToViewportPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));if(p.z<=0)continue;int x=Mathf.Clamp((int)(p.x*960),0,959),y=Mathf.Clamp((int)((1-p.y)*540),0,539);x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x);y1=Math.Max(y1,y);}
        }
        var withHero=Capture(Path.Combine(folder,prefix+"-hero.ppm"));byte[] withoutHero;
        try{foreach(var row in hero)row.Renderer.forceRenderingOff=true;withoutHero=Capture(Path.Combine(folder,prefix+"-background.ppm"));}
        finally{foreach(var row in hero)if(row.Renderer!=null)row.Renderer.forceRenderingOff=row.Hidden;}
        int changed=0;for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){int at=(y*960+x)*3;if(Math.Abs(withHero[at]-withoutHero[at])+Math.Abs(withHero[at+1]-withoutHero[at+1])+Math.Abs(withHero[at+2]-withoutHero[at+2])>12)changed++;}
        return new{ActualSourceHeroRendererCount=hero.Length,Bounds=new[]{x0,y0,x1,y1},ActualHeroContributionPixels=changed,SameFrame=Time.frameCount,Pass=hero.Length>0&&changed>200,Scope="Actual source hero renderers toggled only for same-frame paired native camera/UI captures; changed pixels inside actual projected hero bounds."};
    }
    internal static object LoadingFillPair(string folder,int session)
    {
        var fill=Object.FindObjectsOfType<RawImage>().FirstOrDefault(i=>i.name=="OriginalLoadingBarFill"&&i.gameObject.activeInHierarchy);
        if(fill==null)throw new InvalidOperationException("Actual source fill RawImage absent");
        int[] bounds=null;
        var before=Capture(Path.Combine(folder,"loading-fill-"+session+"-visible.ppm"),fill.rectTransform,b=>bounds=b);bool previous=fill.enabled;byte[] after;
        try{fill.enabled=false;after=Capture(Path.Combine(folder,"loading-fill-"+session+"-hidden.ppm"));}finally{if(fill!=null)fill.enabled=previous;}
        if(bounds==null)throw new InvalidOperationException("Actual fill rectangle was not projected by capture camera");int x0=bounds[0],y0=bounds[1],x1=bounds[2],y1=bounds[3];
        int changed=0;for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){int p=(y*960+x)*3;if(Math.Abs(before[p]-after[p])+Math.Abs(before[p+1]-after[p+1])+Math.Abs(before[p+2]-after[p+2])>12)changed++;}
        int area=Math.Max(0,x1-x0+1)*Math.Max(0,y1-y0+1);
        return new{Session=session,FillRectPixels=new[]{x0,y0,x1,y1},ExpectedFillAreaPixels=area,ActualFillContributionPixels=changed,SameFrame=Time.frameCount,Pass=area>0&&changed>Math.Max(10,area*.25f),Scope="Only actual source fill Graphic toggled in the same native camera/actualCanvas frame; rectangle projected by that actual capture camera after Canvas layout, no global nonzero-pixel test."};
    }
}
