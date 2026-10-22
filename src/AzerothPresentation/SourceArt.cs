using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace AzerothPresentation;

sealed class SourceArt:IDisposable
{
    internal Texture2D Background,Menu,Border,Fill;
    internal SourceArt(string root)
    {
        try{Background=Load(root,"LoadScreenEasternKingdom");Menu=Load(root,"LoadScreenEasternKingdom",true);Border=Load(root,"Loading-BarBorder");Fill=Load(root,"Loading-BarFill");}
        catch{Dispose();throw;}
    }
    static Texture2D Load(string root,string name,bool dark=false)
    {
        var dimensions=File.ReadAllText(Path.Combine(root,name+".size")).Split((char[])null,StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();int width=dimensions[0],height=dimensions[1];
        var bytes=File.ReadAllBytes(Path.Combine(root,name+".rgba"));if(width<1||height<1||bytes.Length!=checked(width*height*4))throw new InvalidDataException("Source RGBA mismatch: "+name);
        // Frozen source exporter already writes FLIP_TOP_BOTTOM PNG rows for
        // native Texture2D RGBA upload. Normal quad/UI UVs need no second flip.
        // Original pixels are retained on disk. This darkened/vignette texture is
        // a documented presentation adaptation based on the observed source menu.
        if(dark)for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {float dx=(x+.5f)/width*2-1,dy=(y+.5f)/height*2-1;float vignette=1-.8f*Mathf.Clamp01((dx*dx+dy*dy)*.5f);int p=(y*width+x)*4;bytes[p]=(byte)(bytes[p]*.4f*vignette);bytes[p+1]=(byte)(bytes[p+1]*.38f*vignette);bytes[p+2]=(byte)(bytes[p+2]*.36f*vignette);}
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false){name="AzerothPresentation_"+name+(dark?"_DarkAdaptation":""),hideFlags=HideFlags.DontUnloadUnusedAsset,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);try{texture.LoadRawTextureData(pin.AddrOfPinnedObject(),bytes.Length);texture.Apply(false,true);}catch{Object.Destroy(texture);throw;}finally{pin.Free();}return texture;
    }
    internal static Mesh Quad()
    {
        var mesh=new Mesh{name="AzerothPresentation_BackdropQuad",hideFlags=HideFlags.DontUnloadUnusedAsset};
        mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
        mesh.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateBounds();return mesh;
    }
    public void Dispose(){foreach(var t in new[]{Background,Menu,Border,Fill})if(t!=null)Object.Destroy(t);Background=Menu=Border=Fill=null;}
}

sealed class LoadingPresentation:IDisposable
{
    internal readonly LoadingScreen Native;readonly GameObject root;readonly RawImage fill;readonly RectTransform fillRect;readonly IntPtr nativeBarPointer;
    internal bool Alive {get{try{return Native!=null&&Native.isActiveAndEnabled&&root!=null;}catch{return false;}}}
    internal float Progress {get{try{return Native?.loadingBar!=null?Mathf.Clamp01(Native.loadingBar.localScale.x):0;}catch{return 0;}}}
    internal LoadingPresentation(LoadingScreen native,SourceArt art)
    {
        Native=native;nativeBarPointer=native.loadingBar!=null?native.loadingBar.Pointer:IntPtr.Zero;root=new GameObject("AzerothPresentation_LoadingCanvas");root.transform.SetParent(native.transform,false);
        var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32700;
        var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        var group=root.AddComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;
        var bg=Image("OriginalEasternKingdom",root.transform,art.Background);bg.rectTransform.anchorMin=Vector2.zero;bg.rectTransform.anchorMax=Vector2.one;bg.rectTransform.offsetMin=bg.rectTransform.offsetMax=Vector2.zero;
        var bar=new GameObject("OriginalLoadingBar");bar.transform.SetParent(root.transform,false);var rect=bar.AddComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.085f);rect.sizeDelta=new Vector2(512,64);
        fill=Image("OriginalLoadingBarFill",bar.transform,art.Fill);fillRect=fill.rectTransform;fillRect.anchorMin=fillRect.anchorMax=new Vector2(0,.5f);fillRect.pivot=new Vector2(0,.5f);fillRect.anchoredPosition=new Vector2(32,0);fillRect.sizeDelta=new Vector2(448,30);
        var border=Image("OriginalLoadingBarBorder",bar.transform,art.Border);border.rectTransform.anchorMin=border.rectTransform.anchorMax=new Vector2(.5f,.5f);border.rectTransform.sizeDelta=new Vector2(512,64);Update();
    }
    static RawImage Image(string name,Transform parent,Texture texture){var go=new GameObject(name);go.transform.SetParent(parent,false);var image=go.AddComponent<RawImage>();image.texture=texture;image.raycastTarget=false;return image;}
    internal void Update(){if(!Alive)return;float value=Progress;fillRect.sizeDelta=new Vector2(448*value,30);fill.uvRect=new Rect(0,0,value,1);}
    internal object Describe()=>new{NativeBarReferencePreserved=Native?.loadingBar!=null&&Native.loadingBar.Pointer==nativeBarPointer,NativeBarPointer=nativeBarPointer.ToInt64(),ProgressSource="LoadingScreen.loadingBar.localScale.x (native async.progress / 0.9)",NativeScaleX=Native?.loadingBar?.localScale.x,Progress,ActualFillWidth=fillRect.rect.width,FullFillWidth=448f,ActualFillUvWidth=fill.uvRect.width,FillGraphicEnabled=fill.enabled&&fill.gameObject.activeInHierarchy,RaycastTargets=root.GetComponentsInChildren<Graphic>(true).Count(g=>g.raycastTarget),GraphicRaycasters=root.GetComponentsInChildren<GraphicRaycaster>(true).Length,SourceArt="Interface/Glues/LoadingScreens/LoadScreenEasternKingdom.blp",SourceFrame="Interface/Glues/LoadingBar/Loading-BarBorder.blp",SourceFill="Interface/Glues/LoadingBar/Loading-BarFill.blp"};
    public void Dispose(){if(root!=null){root.SetActive(false);Object.Destroy(root);}}
}
