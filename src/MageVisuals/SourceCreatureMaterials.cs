using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.MageVisuals;

internal sealed class CreatureMaterialProfile
{
    public string Format {get;set;}
    public string SourceSha256 {get;set;}
    public int SourceDisplayId {get;set;}
    public CreatureMaterialPass[] Passes {get;set;}
}
internal sealed class CreatureMaterialPass
{
    public int SourceSubmesh {get;set;}
    public int SourceMaterialIndex {get;set;}
    public int Flags {get;set;}
    public int Blend {get;set;}
    public int TextureId {get;set;}
    public int[] Triangles {get;set;}
}

internal sealed partial class Model
{
    internal Material[] SourceMaterials;
    internal CreatureMaterialProfile SourceMaterialProfile;
    internal Material[] RenderMaterials=>SourceMaterials??new[]{Material};

    internal void LoadSourceMaterials(string folder,JsonElement model)
    {
        var path=Path.Combine(folder,"source-material-profile.json");
        if(!File.Exists(path))return;
        SourceMaterialProfile=JsonSerializer.Deserialize<CreatureMaterialProfile>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        var profile=SourceMaterialProfile;
        if(profile.Format!="azeroth-creature-material-profile-v1" || profile.SourceDisplayId!=892 ||
           profile.SourceSha256!=model.GetProperty("source").GetProperty("sha256").GetString() ||
           profile.Passes?.Length!=4 || !profile.Passes.Select(p=>p.Blend).SequenceEqual(new[]{0,1,0,2}))
            throw new InvalidDataException("Unverified original Cat material profile");
        var flat=new List<int>();foreach(var pass in profile.Passes)flat.AddRange(pass.Triangles);
        if(!flat.SequenceEqual(model.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32())) ||
           profile.Passes.Any(p=>p.Triangles.Length%3!=0 || p.Triangles.Any(v=>v<0||v>=VertexCount)))
            throw new InvalidDataException("Cat material partition changes the original geometry");
        var shader=Shader.Find("Particles/Standard Unlit");
        if(shader==null)throw new InvalidOperationException("Original source material adapter shader unavailable");
        SourceMaterials=new Material[profile.Passes.Length];Template.subMeshCount=SourceMaterials.Length;
        for(int i=0;i<SourceMaterials.Length;i++)
        {
            var pass=profile.Passes[i];
            Template.SetTriangles(new Il2CppStructArray<int>(pass.Triangles),i,true,0);
            var material=SourceMaterials[i]=Retain(new Material(shader){name="AzerothCat_SourceMaterial_"+i,mainTexture=Texture,color=Color.white});
            material.SetFloat("_Mode",pass.Blend==2?2:pass.Blend==1?1:0);
            material.SetInt("_SrcBlend",(int)(pass.Blend==2?UnityEngine.Rendering.BlendMode.SrcAlpha:UnityEngine.Rendering.BlendMode.One));
            material.SetInt("_DstBlend",(int)(pass.Blend==2?UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha:UnityEngine.Rendering.BlendMode.Zero));
            material.SetInt("_ZWrite",(pass.Flags&16)==0?1:0);
            material.SetInt("_Cull",(int)((pass.Flags&4)!=0?UnityEngine.Rendering.CullMode.Off:UnityEngine.Rendering.CullMode.Back));
            material.SetFloat("_Cutoff",.5f);
            material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_ALPHABLEND_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");material.DisableKeyword("_ALPHAMODULATE_ON");
            if(pass.Blend==1)material.EnableKeyword("_ALPHATEST_ON");
            if(pass.Blend==2)material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue=pass.Blend==2?3000:pass.Blend==1?2450:2000;
        }
        if(Material!=null)Object.Destroy(Material);Material=SourceMaterials[0];
    }
    internal object MaterialDiagnostics()=>new{
        profileLoaded=SourceMaterialProfile!=null,
        materials=RenderMaterials.Select((m,i)=>new{index=i,name=m!=null?m.name:null,shader=m?.shader?.name,
            sourceBlend=SourceMaterialProfile?.Passes[i].Blend,sourceFlags=SourceMaterialProfile?.Passes[i].Flags,
            triangles=Template.GetTriangles(i).Length/3,alphaTest=m!=null&&m.shaderKeywords.Any(k=>k=="_ALPHATEST_ON"),
            alphaBlend=m!=null&&m.shaderKeywords.Any(k=>k=="_ALPHABLEND_ON"),cull=m!=null&&m.HasProperty("_Cull")?m.GetInt("_Cull"):-1,
            zWrite=m!=null&&m.HasProperty("_ZWrite")?m.GetInt("_ZWrite"):-1}).ToArray(),
        submeshes=Template.subMeshCount,frames=Clips.Values.Sum(c=>c.Count),
        allFramesMaterialPartitions=Clips.Values.All(c=>c.Meshes.All(m=>m!=null&&m.subMeshCount==RenderMaterials.Length))};
}

public static partial class MageVisualBridge
{
    public static string SourceMaterialDiagnostics()=>JsonSerializer.Serialize(Plugin.Model?.MaterialDiagnostics());
}
