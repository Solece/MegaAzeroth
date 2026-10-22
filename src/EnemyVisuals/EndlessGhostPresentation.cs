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
    internal const float VisualSizeMultiplier=1f;
    internal const float IncreaseFromPrevious=1.5f;
    internal static bool MatchesId(int id,bool boss=false,bool stageBoss=false,bool finalBoss=false)
        =>!boss && !stageBoss && !finalBoss && id is 16 or 17 or 39 or 40 or 48;
    internal static bool Matches(Enemy enemy)=>enemy?.enemyData!=null && MatchesId((int)enemy.enemyData.enemyName,enemy.IsBoss(),enemy.IsStageBoss(),enemy.IsFinalBoss());
    // An absolute factor against the original native reference, never a
    // multiplication of the previous/pool object's current transform.
    internal static float SizeMultiplier(Enemy enemy,bool boss)
        =>boss?EnemyVisualBridge.BossSizeMultiplier:Matches(enemy)?VisualSizeMultiplier:EnemyVisualBridge.OrdinarySizeMultiplier;
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
    public int[] SourcePassIndices{get;set;}
    public int[] SourceSubmeshes{get;set;}
}
internal sealed class GhostDrawPass
{
    internal string Role;
    internal int SourceGroup;
    internal bool Opaque;
    internal int[] Triangles;
}

internal sealed partial class Model
{
    internal Material[] SourceMaterials;
    internal Material GhostHiddenMaterial;
    internal bool GhostTextureNeutralized;
    internal GhostMaterialProfile GhostProfile;
    internal GhostDrawPass[] GhostDrawPasses;
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
        BuildGhostDrawPasses();
        NeutralizeGhostAtlas(folder);
        // Sprites/Default has fixed alpha blending and cannot guarantee an
        // opaque body even if _SrcBlend/_DstBlend values are assigned.
        var shader=Shader.Find("Particles/Standard Unlit");
        if(shader==null)throw new InvalidOperationException("Opaque source Ghost shader unavailable");
        SourceMaterials=new Material[GhostDrawPasses.Length];Template.subMeshCount=SourceMaterials.Length;
        for(int i=0;i<SourceMaterials.Length;i++)
        {
            var draw=GhostDrawPasses[i];var pass=GhostProfile.Passes[draw.SourceGroup];
            if(pass.Blend is not (0 or 4) || pass.Triangles.Length%3!=0 || pass.Triangles.Any(v=>v<0 || v>=VertexCount))throw new InvalidDataException("Unverified Ghost material/submesh");
            Template.SetTriangles(new Il2CppStructArray<int>(draw.Triangles),i,true,0);
            var material=SourceMaterials[i]=Retain(new Material(shader){name="AzerothGhost_"+draw.Role,mainTexture=Texture,color=Color.white,renderQueue=draw.Opaque?2000:3000});
            material.SetFloat("_Mode",draw.Opaque?0:2);
            material.SetInt("_SrcBlend",(int)(draw.Opaque?UnityEngine.Rendering.BlendMode.One:UnityEngine.Rendering.BlendMode.SrcAlpha));
            material.SetInt("_DstBlend",(int)(draw.Opaque?UnityEngine.Rendering.BlendMode.Zero:UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            material.SetInt("_ZWrite",draw.Opaque?1:0);
            material.SetInt("_Cull",(int)((pass.Flags&4)!=0?UnityEngine.Rendering.CullMode.Off:UnityEngine.Rendering.CullMode.Back));
            material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if(!draw.Opaque)material.EnableKeyword("_ALPHABLEND_ON");else material.DisableKeyword("_ALPHABLEND_ON");
        }
        if(Material!=null)Object.Destroy(Material);Material=SourceMaterials[0];
        GhostHiddenMaterial=Retain(new Material(shader){name="AzerothGhost_SourceZeroOpacity",mainTexture=Texture,color=Color.clear,renderQueue=3000});
        GhostHiddenMaterial.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.Zero);
        GhostHiddenMaterial.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.One);
        GhostHiddenMaterial.SetInt("_ZWrite",0);
        GhostHiddenMaterial.EnableKeyword("_ALPHABLEND_ON");
    }
    void BuildGhostDrawPasses()
    {
        // Original Ghost M2 SHA pins the direct, unremapped vertex ordering.
        // Source submeshes0..11: actual3D body, vertices0..896,944triangles.
        // Submeshes12..23: optional trailing wisps, vertices897..947,30triangles.
        // Submesh24: flat death-glow quad;25: original black death orb.
        const string expectedSource="6f0bad96f2205b44dac625a6222b6c2306f9ccd336b3cf732a89891289a0ca5b";
        if(GhostProfile.SourceSha256!=expectedSource || VertexCount!=1094 || GhostProfile.Passes.Length!=3
            || !GhostProfile.Passes[0].SourcePassIndices.SequenceEqual(Enumerable.Range(0,24))
            || !GhostProfile.Passes[0].SourceSubmeshes.SequenceEqual(Enumerable.Range(0,24))
            || GhostProfile.Passes[0].Flags!=21 || GhostProfile.Passes[0].Blend!=4
            || !GhostProfile.Passes[1].SourcePassIndices.SequenceEqual(new[]{24})
            || !GhostProfile.Passes[2].SourcePassIndices.SequenceEqual(new[]{25}))throw new InvalidDataException("Source Ghost body/glow ownership is not verified");
        var body=new List<int>();var wisps=new List<int>();var all=GhostProfile.Passes[0].Triangles;
        for(int i=0;i<all.Length;i+=3)
        {
            int a=all[i],b=all[i+1],c=all[i+2];bool isBody=a<897 && b<897 && c<897;bool isWisp=a>=897 && a<948 && b>=897 && b<948 && c>=897 && c<948;
            if(!isBody && !isWisp)throw new InvalidDataException("Source Ghost body triangle crosses glow ownership");
            var selected=isBody?body:wisps;selected.Add(a);selected.Add(b);selected.Add(c);
        }
        if(body.Count!=944*3 || wisps.Count!=30*3 || GhostProfile.Passes[1].Triangles.Length!=6 || GhostProfile.Passes[2].Triangles.Length!=180*3)
            throw new InvalidDataException("Source Ghost body/glow geometry count mismatch");
        GhostDrawPasses=new[]{
            new GhostDrawPass{Role="OpaqueBody",SourceGroup=0,Opaque=true,Triangles=body.ToArray()},
            new GhostDrawPass{Role="OptionalSourceWisps",SourceGroup=0,Triangles=wisps.ToArray()},
            new GhostDrawPass{Role="SourceDeathGlow",SourceGroup=1,Triangles=GhostProfile.Passes[1].Triangles},
            new GhostDrawPass{Role="SourceDeathOrb",SourceGroup=2,Opaque=true,Triangles=GhostProfile.Passes[2].Triangles}
        };
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
    internal Color GhostDrawColor(string clip,int frame,int draw,Color nativeTint)
    {
        var pass=GhostDrawPasses[draw];var source=GhostPaletteSourceColor(clip,frame,pass.SourceGroup);
        // Body RGB and native phase/status tint stay byte-for-byte on the same
        // color path. Body opacity is binary; optional glow keeps source alpha.
        return new Color(source.r*nativeTint.r,source.g*nativeTint.g,source.b*nativeTint.b,pass.Opaque?(source.a>0?1:0):source.a);
    }
    internal object GhostRenderSnapshot(MeshRenderer renderer,string clip,int frame,Color nativeTint)
        =>GhostDrawPasses.Select((p,i)=>new{Role=p.Role,SourceGroup=p.SourceGroup,Opaque=p.Opaque,Triangles=p.Triangles.Length/3,
            Shader=SourceMaterials[i].shader.name,SrcBlend=SourceMaterials[i].GetInt("_SrcBlend"),DstBlend=SourceMaterials[i].GetInt("_DstBlend"),ZWrite=SourceMaterials[i].GetInt("_ZWrite"),RenderQueue=SourceMaterials[i].renderQueue,
            Color=new[]{GhostDrawColor(clip,frame,i,nativeTint).r,GhostDrawColor(clip,frame,i,nativeTint).g,GhostDrawColor(clip,frame,i,nativeTint).b,GhostDrawColor(clip,frame,i,nativeTint).a}}).ToArray();
    internal void ApplyGhostColors(MeshRenderer renderer,string clip,int frame,Color nativeTint,ref MaterialPropertyBlock[] blocks,ref Material[] chosen)
    {
        if(GhostProfile==null || renderer==null)return;
        if(blocks==null || blocks.Length!=SourceMaterials.Length)blocks=Enumerable.Range(0,SourceMaterials.Length).Select(_=>new MaterialPropertyBlock()).ToArray();
        bool changed=chosen==null;if(chosen==null)chosen=SourceMaterials.ToArray();
        for(int i=0;i<blocks.Length;i++)
        {
            var source=GhostPaletteSourceColor(clip,frame,GhostDrawPasses[i].SourceGroup);
            // Original zero-opacity frames skip a draw altogether. Opaque
            // body/death-orb draws use binary visibility at that same boundary;
            // optional glow alone retains fractional source opacity.
            var material=GhostDrawPasses[i].Opaque && source.a<=0?GhostHiddenMaterial:SourceMaterials[i];
            if(chosen[i]!=material){chosen[i]=material;changed=true;}
            var color=GhostDrawColor(clip,frame,i,nativeTint);
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
        var expected=model.GhostDrawColor(clipName,lastFrame,0,nativeGhostTint);
        return new{Attached=true,EnemyId=enemy!=null?enemy.id:0,NativeEnemyId=enemy?.enemyData!=null?(int)enemy.enemyData.enemyName:-1,NativeEnemyName=enemy?.enemyData?.enemyName.ToString(),SourceModel=model.Key,SourceDisplayId=146,
            PaletteAvailable=ghostPaletteAvailable,NativeColorSource=ghostColorSource,NativeAlbedoKey=ghostAlbedoKey,NativeSpecularKey=ghostSpecularKey,NativeRimKey=ghostRimKey,
            NativeAlbedo=Rgba(nativeGhostAlbedo),NativeTexture=ghostNativeTexture,NativePaletteTint=Rgba(nativeGhostTint),SourceAtlasLuminanceAlpha=model.GhostTextureNeutralized,NativeSpecular=Rgba(nativeGhostSpecular),NativeRim=Rgba(nativeGhostRim),SourceBaseColor=Rgba(source),ExpectedSourceColor=Rgba(expected),ObservedSourceColor=Rgba(observed),
            PaletteChanges=ghostPaletteChanges,PassCount=model.SourceMaterials.Length,SourcePassCount=model.GhostProfile.SourcePassCount,SourceEmbeddedParticles=model.GhostProfile.SourceEmbeddedParticles,SourceEmbeddedParticlesRendered=false,
            MaterialBlends=model.GhostProfile.Passes.Select([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (p)=>p.Blend).ToArray(),RuntimePaletteBlends=model.GhostDrawPasses.Select([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (p)=>p.Opaque?"One/Zero":"SrcAlpha/OneMinusSrcAlpha").ToArray(),TwoSidedPasses=model.GhostProfile.Passes.Count([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (p)=>(p.Flags&4)!=0),
            OpaqueBody=true,OptionalGlowPreservesSourceAlpha=true,SizeMultiplier=sizeMultiplier,IncreaseFromPrevious=EndlessGhostPresentation.IncreaseFromPrevious,DrawPasses=model.GhostRenderSnapshot(replacement,clipName,lastFrame,nativeGhostTint),
            NativeBodyHidden=original!=null&&!original.enabled,Visible=replacement!=null&&replacement.enabled&&replacement.gameObject.activeInHierarchy,IsBoss=isBoss};
    }
}

public static partial class EnemyVisualBridge
{
    public static string DescribeGhostPalette(Enemy enemy)=>JsonSerializer.Serialize(EnemyVisualOwnership.Find(enemy)?.GhostPaletteSnapshot()??new{Attached=false});
    public static string GhostPaletteDiagnostics()=>JsonSerializer.Serialize(new{
        NativeSwarmTiers=new[]{16,17,40,39},AdditionalNativeGhostId=48,SourceDisplayId=146,SourceModel="Creature\\Ghost\\Ghost.m2",
        OpaqueBody=true,VisualSizeMultiplier=EndlessGhostPresentation.VisualSizeMultiplier,IncreaseFromPrevious=EndlessGhostPresentation.IncreaseFromPrevious,
        Policy="Exact source3D body is opaque with depth writes, optional wisps/death glow retain source alpha. Source atlas luminance times actual native phase/status RGB remains unchanged. Exact nonboss ghost IDs scale1.5 from old2/3 to1native reference; no native gameplay or source files change.",
        NativeTextureCache=NativeGhostPaletteCache.Snapshot(),
        Reads=EndlessGhostPresentation.Reads,Changes=EndlessGhostPresentation.Changes,Failures=EndlessGhostPresentation.Failures,
        Ghosts=Object.FindObjectsOfType<AzerothVisual>(true).Where(v=>v!=null&&v.IsGhostVisual).Select(v=>v.GhostPaletteSnapshot()).ToArray()});
    public static string RunGhostPresentationRegressionForProbe()
    {
        if(!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1")return "{\"allowed\":false}";
        var model=Plugin.GetModel("ghost");if(model?.GhostProfile==null)throw new InvalidOperationException("Original Ghost pack unavailable");int checks=0;
        void Check(bool value,string label){if(!value)throw new InvalidOperationException("Ghost presentation regression: "+label);checks++;}
        for(int id=-1;id<=64;id++)
        {
            bool expected=id is 16 or 17 or 39 or 40 or 48;Check(EndlessGhostPresentation.MatchesId(id)==expected,"ordinary exact native ID "+id);
            Check(!EndlessGhostPresentation.MatchesId(id,true),"boss excluded "+id);Check(!EndlessGhostPresentation.MatchesId(id,false,true),"stage boss excluded "+id);Check(!EndlessGhostPresentation.MatchesId(id,false,false,true),"final boss excluded "+id);
        }
        Check(!EndlessGhostPresentation.MatchesId(43),"GhostKing43 excluded");
        Check(Math.Abs(EndlessGhostPresentation.VisualSizeMultiplier/OrdinarySizeMultiplier-1.5f)<.00001f,"exact1.5 visual increase");
        Check(model.SourceMaterials.Length==4 && model.GhostDrawPasses.Length==4,"separate body/wisps/death-glow/orb draws");
        Check(model.GhostDrawPasses[0].Triangles.Length==944*3,"all original3D body triangles");
        Check(model.GhostDrawPasses[1].Triangles.Length==30*3 && model.GhostDrawPasses[1].Triangles.All(v=>v>=897 && v<948),"only source optional wisps remain transparent");
        Check(model.GhostDrawPasses[2].Triangles.Length==6 && !model.GhostDrawPasses[2].Opaque,"flat death glow retains source alpha");
        Check(model.GhostDrawPasses[3].Triangles.Length==180*3,"original death orb retained");
        Check(model.GhostDrawPasses[0].Triangles.Concat(model.GhostDrawPasses[1].Triangles).SequenceEqual(model.GhostProfile.Passes[0].Triangles),"original body+wisps triangle order and ownership");
        var body=model.SourceMaterials[0];Check(body.GetInt("_SrcBlend")==1 && body.GetInt("_DstBlend")==0 && body.GetInt("_ZWrite")==1 && body.renderQueue==2000,"body is truly opaque and writes depth");
        // The generated IsKeywordEnabled(string) wrapper uses unsupported
        // Il2Cpp ReadOnlySpan pinning on this host. The keyword array getter
        // reads the same material state without that managed span bridge.
        var keywords=body.shaderKeywords;bool alphaBlend=false,alphaTest=false;
        if(keywords!=null)for(int i=0;i<keywords.Length;i++){string keyword=keywords[i];alphaBlend|=keyword=="_ALPHABLEND_ON";alphaTest|=keyword=="_ALPHATEST_ON";}
        Check(!alphaBlend && !alphaTest,"body has no soft alpha or cutout keyword");
        // Deliberate test vector, not an invented native tier palette.
        var tint=new Color(.2f,.4f,.8f,.1f);
        foreach(var entry in model.GhostProfile.Clips)
        foreach(int f in new[]{0,entry.Value.Length/2,entry.Value.Length-1})
        for(int draw=0;draw<model.GhostDrawPasses.Length;draw++)
        {
            var pass=model.GhostDrawPasses[draw];var source=model.GhostPaletteSourceColor(entry.Key,f,pass.SourceGroup);var actual=model.GhostDrawColor(entry.Key,f,draw,tint);
            Check(Math.Abs(actual.r-source.r*tint.r)<.00001f && Math.Abs(actual.g-source.g*tint.g)<.00001f && Math.Abs(actual.b-source.b*tint.b)<.00001f,"unchanged source luminance/native RGB "+entry.Key+"/"+f+"/"+draw);
            Check(Math.Abs(actual.a-(pass.Opaque?(source.a>0?1:0):source.a))<.00001f,"opaque body/binary visibility and optional glow opacity "+entry.Key+"/"+f+"/"+draw);
        }
        Check(model.IsUsable(),"source mesh/material/texture cache retained");
        return JsonSerializer.Serialize(new{allowed=true,pass=true,checks,OpaqueBody=true,SourceDisplayId=146,VisualSizeMultiplier=EndlessGhostPresentation.VisualSizeMultiplier,IncreaseFromPrevious=EndlessGhostPresentation.IncreaseFromPrevious,DrawPasses=model.GhostRenderSnapshot(null,"idle",0,Color.white)});
    }
}
