using System.Text.Json;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.EnemyVisuals;

// The installed native tier materials store their base colors in _AlbedoMap,
// not _AlbedoColor (the latter is a debuff modifier). Never change that texture.
internal static class NativeGhostPaletteCache
{
    internal const int MaximumSampleDimension=256;
    internal sealed class Entry
    {
        public int TextureId {get;set;}
        public string TextureName {get;set;}
        public int Width {get;set;}
        public int Height {get;set;}
        public int ReadWidth {get;set;}
        public int ReadHeight {get;set;}
        public int SampleCount {get;set;}
        public double AlphaWeight {get;set;}
        public float[] MeanLinear {get;set;}
        public bool Valid {get;set;}
        public string Failure {get;set;}
        public bool ReadbackAlive=>Readback!=null&&Readback.width>0&&Readback.height>0;
        public bool ReadbackRetained=>ReadbackAlive&&(Readback.hideFlags&HideFlags.DontUnloadUnusedAsset)!=0;
        internal Color Color;
        internal Texture2D Readback;
    }
    static readonly Dictionary<(long,int),Entry> entries=new();
    internal static int ReadAttempts,ReadFailures,CacheHits;
    internal static Entry Get(Texture texture)
    {
        if(texture==null)return null;
        var key=(texture.Pointer.ToInt64(),texture.GetInstanceID());
        if(entries.TryGetValue(key,out var cached)){CacheHits++;return cached;}
        var entry=new Entry{TextureId=key.Item2,TextureName=texture.name,Width=texture.width,Height=texture.height};
        entries.Add(key,entry);ReadAttempts++;
        RenderTexture rt=null;Texture2D readback=null;
        var previous=RenderTexture.active;bool previousSrgbWrite=GL.sRGBWrite;
        try
        {
            entry.ReadWidth=Math.Clamp(texture.width,1,MaximumSampleDimension);
            entry.ReadHeight=Math.Clamp(texture.height,1,MaximumSampleDimension);
            rt=new RenderTexture(entry.ReadWidth,entry.ReadHeight,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            rt.name="AzerothGhost_PaletteReadTemporary";rt.Create();
            // The native sRGB source is decoded by the texture sampler. Prevent
            // an extra target sRGB encode, then read RGBA bytes as linear data.
            GL.sRGBWrite=false;Graphics.Blit(texture,rt);RenderTexture.active=rt;
            readback=new Texture2D(entry.ReadWidth,entry.ReadHeight,TextureFormat.RGBA32,false,true);
            readback.name="AzerothGhost_NativePalette_"+entry.TextureId;
            readback.hideFlags|=HideFlags.DontUnloadUnusedAsset;
            readback.ReadPixels(new Rect(0,0,entry.ReadWidth,entry.ReadHeight),0,0);readback.Apply(false,false);
            var pixels=readback.GetPixels32();double r=0,g=0,b=0,weight=0;
            foreach(var pixel in pixels)
            {double a=pixel.a/255.0;r+=pixel.r/255.0*a;g+=pixel.g/255.0*a;b+=pixel.b/255.0*a;weight+=a;}
            if(weight<=0)throw new InvalidDataException("Actual native ghost albedo map has no nonzero-alpha samples");
            entry.SampleCount=pixels.Length;entry.AlphaWeight=weight;
            entry.Color=new Color((float)(r/weight),(float)(g/weight),(float)(b/weight),1);
            entry.MeanLinear=Rgba(entry.Color);entry.Readback=readback;readback=null;entry.Valid=true;
        }
        catch(Exception error)
        {ReadFailures++;entry.Failure=error.GetType().Name+": "+error.Message;EndlessGhostPresentation.Failure(error);}
        finally
        {
            GL.sRGBWrite=previousSrgbWrite;RenderTexture.active=previous;
            if(rt!=null){rt.Release();Object.Destroy(rt);}
            if(readback!=null)Object.Destroy(readback);
        }
        return entry;
    }
    internal static float[] Rgba(Color color)=>new[]{color.r,color.g,color.b,color.a};
    internal static object Snapshot()=>new{
        Policy="Actual _AlbedoMap alpha-weighted linear RGB mean; one bounded GPU Blit/ReadPixels per texture identity, cached for module lifetime; original native assets unmodified",
        MaximumSampleDimension,ReadAttempts,ReadFailures,CacheHits,Textures=entries.Values.ToArray()};
    internal static void Shutdown()
    {
        foreach(var entry in entries.Values)if(entry.Readback!=null)Object.Destroy(entry.Readback);
        entries.Clear();ReadAttempts=ReadFailures=CacheHits=0;
    }
}

public static partial class EnemyVisualBridge
{
    public static string GhostNativeTextureDiagnostics()=>JsonSerializer.Serialize(NativeGhostPaletteCache.Snapshot());
}
