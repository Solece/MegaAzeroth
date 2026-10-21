using System.Text.Json;
using System.Runtime.InteropServices;
using Assets.Scripts.Actors.Enemies;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.EnemyVisuals;

internal static class EndlessGhostPresentation
{
    // SwarmSummoner ctor RVA489AB0 starts16; TickExtra RVA489860 advances
    // native tier0->1->2->3 through17,40,39.48 is a separate native invincible
    // ghost, supported as the same visual family but not claimed as a swarm tier.
    internal static bool Matches(Enemy enemy)=>enemy?.enemyData!=null && !enemy.IsBoss() && !enemy.IsStageBoss() && !enemy.IsFinalBoss()
        && (int)enemy.enemyData.enemyName is 16 or 17 or 39 or 40 or 48;
    internal static int Reads,Changes,Failures;
    internal static void Failure(Exception error){Failures++;if(Failures<=5)Plugin.Logger.LogWarning("Source ghost native palette read failed: "+error);}
}

internal sealed class GhostMaterialProfile
{
    public string Format{get;set;}
    public string SourceSha256{get;set;}
    public GhostMaterialPass[] Passes{get;set;}
    public Dictionary<string,float[][][]> Clips{get;set;}
    public int SourceEmbeddedParticles{get;set;}
    public int SourcePassCount{get;set;}
}
internal sealed class GhostMaterialPass
{
    public int Flags{get;set;}
    public int Blend{get;set;}
    public int[] Triangles{get;set;}
}

internal sealed partial class Model
{
    internal Material[] SourceMaterials;
    internal Material GhostHiddenMaterial;
    internal bool GhostTextureNeutralized;
    internal GhostMaterialProfile GhostProfile;
    internal Material[] RenderMaterials=>SourceMaterials??new[]{Material};
    internal void LoadGhostProfile(string folder)
    {
        if(Key!="ghost")return;
        var path=Path.Combine(folder,"ghost-material-profile.json");
        if(!File.Exists(path))throw new InvalidDataException("Original Ghost material profile missing");
        GhostProfile=JsonSerializer.Deserialize<GhostMaterialProfile>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        if(GhostProfile.Format!="azeroth-ghost-material-v1" || GhostProfile.Passes.Length==0)throw new InvalidDataException("Invalid original Ghost render profile");
        using var model=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"model.json")));
        if(GhostProfile.SourceSha256!=model.RootElement.GetProperty("source").GetProperty("sha256").GetString())throw new InvalidDataException("Ghost material/model source hash mismatch");
        NeutralizeGhostAtlas(folder);
        var shader=Shader.Find("Particles/Standard Unlit")??Shader.Find("Sprites/Default");
        if(shader==null)throw new InvalidOperationException("Source Ghost additive shader unavailable");
        SourceMaterials=new Material[GhostProfile.Passes.Length];Template.subMeshCount=SourceMaterials.Length;
        for(int i=0;i<SourceMaterials.Length;i++)
        {
            var pass=GhostProfile.Passes[i];
            if(pass.Blend is not (0 or 4) || pass.Triangles.Length%3!=0 || pass.Triangles.Any(v=>v<0 || v>=VertexCount))throw new InvalidDataException("Unverified Ghost material/submesh");
            Template.SetTriangles(new Il2CppStructArray<int>(pass.Triangles),i,true,0);
            var material=SourceMaterials[i]=Retain(new Material(shader){name="AzerothGhost_SourcePass_"+i,mainTexture=Texture,color=Color.white,renderQueue=pass.Blend==0?2000:3000});
            material.SetFloat("_Mode",pass.Blend==4?2:0);
            material.SetInt("_SrcBlend",(int)(pass.Blend==4?UnityEngine.Rendering.BlendMode.SrcAlpha:UnityEngine.Rendering.BlendMode.One));
            material.SetInt("_DstBlend",(int)(pass.Blend==4?UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha:UnityEngine.Rendering.BlendMode.Zero));
            material.SetInt("_ZWrite",(pass.Flags&16)==0?1:0);
            material.SetInt("_Cull",(int)((pass.Flags&4)!=0?UnityEngine.Rendering.CullMode.Off:UnityEngine.Rendering.CullMode.Back));
            material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if(pass.Blend==4)material.EnableKeyword("_ALPHABLEND_ON");else material.DisableKeyword("_ALPHABLEND_ON");
        }
        if(Material!=null)Object.Destroy(Material);Material=SourceMaterials[0];
        GhostHiddenMaterial=Retain(new Material(shader){name="AzerothGhost_SourceZeroOpacity",mainTexture=Texture,color=Color.clear,renderQueue=3000});
        GhostHiddenMaterial.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.Zero);
        GhostHiddenMaterial.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.One);
        GhostHiddenMaterial.SetInt("_ZWrite",0);
        GhostHiddenMaterial.EnableKeyword("_ALPHABLEND_ON");
    }
    void NeutralizeGhostAtlas(string folder)
    {
        byte[] rgba=File.ReadAllBytes(Path.Combine(folder,"texture.rgba"));
        if(rgba.Length!=Texture.width*Texture.height*4)throw new InvalidDataException("Source Ghost neutral atlas size mismatch");
        for(int i=0;i<rgba.Length;i+=4)
        {
            float luminance=.2126f*Mathf.GammaToLinearSpace(rgba[i]/255f)+.7152f*Mathf.GammaToLinearSpace(rgba[i+1]/255f)+.0722f*Mathf.GammaToLinearSpace(rgba[i+2]/255f);
            byte gray=(byte)Math.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(luminance)*255),0,255);
            rgba[i]=rgba[i+1]=rgba[i+2]=gray;
        }
        var old=Texture;
        Texture=Retain(new Texture2D(old.width,old.height,TextureFormat.RGBA32,false){name="AzerothGhost_SourceLuminanceAlpha",filterMode=old.filterMode,wrapMode=old.wrapMode});
        var pin=GCHandle.Alloc(rgba,GCHandleType.Pinned);
        try{Texture.LoadRawTextureData(pin.AddrOfPinnedObject(),rgba.Length);Texture.Apply(false,true);}finally{pin.Free();}
        Object.Destroy(old);GhostTextureNeutralized=true;
    }
    internal Color GhostPaletteSourceColor(string clip,int frame,int pass)
    {
        var source=GhostColor(clip,frame,pass);float luminance=.2126f*source.r+.7152f*source.g+.0722f*source.b;
        return new Color(luminance,luminance,luminance,source.a);
    }
    internal Color GhostColor(string clip,int frame,int pass)
    {
        if(GhostProfile==null)return Color.white;
        if(!GhostProfile.Clips.TryGetValue(clip??"idle",out var frames))frames=GhostProfile.Clips["idle"];
        var rgba=frames[Math.Clamp(frame,0,frames.Length-1)][pass];return new Color(rgba[0],rgba[1],rgba[2],rgba[3]);
    }
    internal void ApplyGhostColors(MeshRenderer renderer,string clip,int frame,Color nativeTint,ref MaterialPropertyBlock[] blocks,ref Material[] chosen)
    {
        if(GhostProfile==null || renderer==null)return;
        if(blocks==null || blocks.Length!=SourceMaterials.Length)blocks=Enumerable.Range(0,SourceMaterials.Length).Select(_=>new MaterialPropertyBlock()).ToArray();
        bool changed=chosen==null;if(chosen==null)chosen=SourceMaterials.ToArray();
        for(int i=0;i<blocks.Length;i++)
        {
            var source=GhostPaletteSourceColor(clip,frame,i);
            // Source M2 skips a zero-opacity opaque pass entirely. An opaque
            // Unity pass ignores alpha; a no-color/no-depth variant prevents
            // the source death-only opaque geometry from appearing while alive.
            var material=GhostProfile.Passes[i].Blend==0 && source.a<=0?GhostHiddenMaterial:SourceMaterials[i];
            if(chosen[i]!=material){chosen[i]=material;changed=true;}
            // Recolor RGB from native state; preserve original source M2 alpha.
            var color=new Color(source.r*nativeTint.r,source.g*nativeTint.g,source.b*nativeTint.b,source.a);
            if(!blocks[i].HasColor("_Color") || blocks[i].GetColor("_Color")!=color)
            {blocks[i].SetColor("_Color",color);renderer.SetPropertyBlock(blocks[i],i);}
        }
        if(changed)renderer.sharedMaterials=chosen;
    }
}

public sealed partial class AzerothVisual
{
    [HideFromIl2Cpp] internal bool IsGhostVisual { [HideFromIl2Cpp] get=>model?.GhostProfile!=null; }
    [HideFromIl2Cpp] internal Color GhostTint { [HideFromIl2Cpp] get=>nativeGhostTint; }
    MaterialPropertyBlock ghostNativeBlock,ghostNativeRendererBlock;
    MaterialPropertyBlock[] ghostSourceBlocks;
    Material[] ghostChosenMaterials;
    Color nativeGhostAlbedo=Color.white,nativeGhostSpecular,nativeGhostRim,nativeGhostTint=Color.white;
    NativeGhostPaletteCache.Entry ghostNativeTexture;
    string ghostAlbedoKey="",ghostSpecularKey="",ghostRimKey="",ghostColorSource="";
    bool ghostPaletteAvailable;int ghostReadFrame=-1,ghostPaletteChanges;
    [HideFromIl2Cpp]
    void ResetGhostPalette(){ghostReadFrame=-1;ghostPaletteAvailable=false;ghostSourceBlocks=null;ghostChosenMaterials=null;ghostPaletteChanges=0;nativeGhostAlbedo=Color.white;nativeGhostTint=Color.white;ghostNativeTexture=null;}
    [HideFromIl2Cpp]
    void SyncGhostPalette()
    {
        if(model?.GhostProfile==null || original==null || enemy==null)return;
        try
        {
            if(ghostReadFrame!=Time.frameCount)
            {
                ghostReadFrame=Time.frameCount;var controller=enemy.enemyRenderer;
                if(controller==null)return;
                // Native constructor RVA334F10 binds these to _AlbedoColor,
                // _SpecularColor and its actual rim key. Read the fields rather
                // than assuming a palette for each named enemy variant.
                ghostAlbedoKey=controller.albedoColorKey;ghostSpecularKey=controller.specularColorKey;ghostRimKey=controller.rimColorKey;
                var reference=controller.enemyRenderer??original;
                ghostNativeBlock??=new MaterialPropertyBlock();reference.GetPropertyBlock(ghostNativeBlock,0);
                ghostNativeRendererBlock??=new MaterialPropertyBlock();reference.GetPropertyBlock(ghostNativeRendererBlock);
                [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] bool Read(string key,out Color value,out string origin)
                {
                    value=Color.white;origin="";if(string.IsNullOrEmpty(key))return false;
                    if(ghostNativeBlock.HasColor(key)){value=ghostNativeBlock.GetColor(key);origin="native-material0-property-block:"+key;return true;}
                    if(ghostNativeRendererBlock.HasColor(key)){value=ghostNativeRendererBlock.GetColor(key);origin="native-renderer-property-block:"+key;return true;}
                    var material=reference.sharedMaterial;
                    if(material!=null && material.HasProperty(key)){value=material.GetColor(key);origin="native-renderer-material:"+key;return true;}
                    material=enemy.enemyData?.material;
                    if(material!=null && material.HasProperty(key)){value=material.GetColor(key);origin="native-enemy-data-material:"+key;return true;}
                    return false;
                }
                bool modifierAvailable=Read(ghostAlbedoKey,out var albedo,out var source);
                nativeGhostAlbedo=albedo;
                var actualTexture=ghostNativeBlock.GetTexture("_AlbedoMap");
                string textureOrigin="native-material0-property-block:_AlbedoMap";
                if(actualTexture==null){actualTexture=ghostNativeRendererBlock.GetTexture("_AlbedoMap");textureOrigin="native-renderer-property-block:_AlbedoMap";}
                if(actualTexture==null)
                {
                    var material=reference.sharedMaterial??enemy.enemyData?.material;
                    if(material!=null&&material.HasProperty("_AlbedoMap"))actualTexture=material.GetTexture("_AlbedoMap");
                    textureOrigin="native-renderer-material:_AlbedoMap";
                }
                ghostNativeTexture=NativeGhostPaletteCache.Get(actualTexture);
                ghostPaletteAvailable=modifierAvailable&&ghostNativeTexture?.Valid==true;
                if(ghostPaletteAvailable)
                {
                    var sampled=ghostNativeTexture.Color;
                    var tint=new Color(sampled.r*albedo.r,sampled.g*albedo.g,sampled.b*albedo.b,1);
                    if(ghostPaletteChanges==0||tint!=nativeGhostTint){ghostPaletteChanges++;EndlessGhostPresentation.Changes++;}
                    nativeGhostTint=tint;ghostColorSource=textureOrigin+" * "+source;
                }
                Read(ghostSpecularKey,out nativeGhostSpecular,out _);Read(ghostRimKey,out nativeGhostRim,out _);EndlessGhostPresentation.Reads++;
            }
            model.ApplyGhostColors(replacement,clipName,lastFrame,nativeGhostTint,ref ghostSourceBlocks,ref ghostChosenMaterials);
        }
        catch(Exception error){EndlessGhostPresentation.Failure(error);}
    }
    [HideFromIl2Cpp]
    internal object GhostPaletteSnapshot()
    {
        if(model?.GhostProfile==null)return new{Attached=false};
        SyncGhostPalette();var source=model.GhostPaletteSourceColor(clipName,lastFrame,0);
        var observed=ghostSourceBlocks?.Length>0?ghostSourceBlocks[0].GetColor("_Color"):Color.white;
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] float[] Rgba(Color c)=>new[]{c.r,c.g,c.b,c.a};
        var expected=new Color(source.r*nativeGhostTint.r,source.g*nativeGhostTint.g,source.b*nativeGhostTint.b,source.a);
        return new{Attached=true,EnemyId=enemy!=null?enemy.id:0,NativeEnemyId=enemy?.enemyData!=null?(int)enemy.enemyData.enemyName:-1,NativeEnemyName=enemy?.enemyData?.enemyName.ToString(),SourceModel=model.Key,SourceDisplayId=146,
            PaletteAvailable=ghostPaletteAvailable,NativeColorSource=ghostColorSource,NativeAlbedoKey=ghostAlbedoKey,NativeSpecularKey=ghostSpecularKey,NativeRimKey=ghostRimKey,
            NativeAlbedo=Rgba(nativeGhostAlbedo),NativeTexture=ghostNativeTexture,NativePaletteTint=Rgba(nativeGhostTint),SourceAtlasLuminanceAlpha=model.GhostTextureNeutralized,NativeSpecular=Rgba(nativeGhostSpecular),NativeRim=Rgba(nativeGhostRim),SourceBaseColor=Rgba(source),ExpectedSourceColor=Rgba(expected),ObservedSourceColor=Rgba(observed),
            PaletteChanges=ghostPaletteChanges,PassCount=model.SourceMaterials.Length,SourcePassCount=model.GhostProfile.SourcePassCount,SourceEmbeddedParticles=model.GhostProfile.SourceEmbeddedParticles,SourceEmbeddedParticlesRendered=false,
            MaterialBlends=model.GhostProfile.Passes.Select([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (p)=>p.Blend).ToArray(),RuntimePaletteBlends=model.GhostProfile.Passes.Select([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (p)=>p.Blend==4?"SrcAlpha/OneMinusSrcAlpha":"One/Zero").ToArray(),TwoSidedPasses=model.GhostProfile.Passes.Count([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (p)=>(p.Flags&4)!=0),
            NativeBodyHidden=original!=null&&!original.enabled,Visible=replacement!=null&&replacement.enabled&&replacement.gameObject.activeInHierarchy,IsBoss=isBoss};
    }
}

public static partial class EnemyVisualBridge
{
    public static string DescribeGhostPalette(Enemy enemy)=>JsonSerializer.Serialize(EnemyVisualOwnership.Find(enemy)?.GhostPaletteSnapshot()??new{Attached=false});
    public static string GhostPaletteDiagnostics()=>JsonSerializer.Serialize(new{
        NativeSwarmTiers=new[]{16,17,40,39},AdditionalNativeGhostId=48,SourceDisplayId=146,SourceModel="Creature\\Ghost\\Ghost.m2",
        Policy="Source M2 geometry/opacity, source atlas luminance, actual native _AlbedoMap mean times status albedo; alpha-over palette adapter makes native dark textures visible. Original source/native assets and gameplay unchanged.",
        NativeTextureCache=NativeGhostPaletteCache.Snapshot(),
        Reads=EndlessGhostPresentation.Reads,Changes=EndlessGhostPresentation.Changes,Failures=EndlessGhostPresentation.Failures,
        Ghosts=Object.FindObjectsOfType<AzerothVisual>(true).Where(v=>v!=null&&v.IsGhostVisual).Select(v=>v.GhostPaletteSnapshot()).ToArray()});
}
