using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.EnemyVisuals;

internal sealed class ModelAssetHealth
{
    public bool Valid {get;set;}
    public string Name {get;set;}
    public int SourceVertexCount {get;set;}
    public int ExpectedMeshes {get;set;}
    public int AliveMeshes {get;set;}
    public int RetainedMeshes {get;set;}
    public int ClipCount {get;set;}
    public bool TextureAlive {get;set;}
    public bool MaterialAlive {get;set;}
    public string[] Problems {get;set;}
}

internal sealed partial class Model
{
    internal string Key="";
    internal int VertexCount;
    internal static T Retain<T>(T asset) where T:Object
    {
        if(asset!=null)asset.hideFlags|=HideFlags.DontUnloadUnusedAsset;
        return asset;
    }
    internal bool IsUsable(bool allFrames=true)=>Inspect(allFrames).Valid;
    internal ModelAssetHealth Inspect(bool allFrames=true)
    {
        var errors=new List<string>();int expected=1+Clips.Values.Sum(c=>c.Count),alive=0,retained=0;
        void Check(Mesh mesh,string label)
        {
            try
            {
                if(mesh==null){errors.Add(label+":destroyed");return;}
                int count=mesh.vertexCount;alive++;
                if(count!=VertexCount)errors.Add(label+":vertexCount="+count);
                if((mesh.hideFlags&HideFlags.DontUnloadUnusedAsset)!=0)retained++;else errors.Add(label+":not-retained");
            }
            catch(Exception ex){errors.Add(label+":"+ex.GetType().Name);}
        }
        Check(Template,"template");
        if(allFrames)foreach(var pair in Clips)
        {
            var c=pair.Value;if(c.Meshes==null || c.Meshes.Length!=c.Count){errors.Add(pair.Key+":frame-array");continue;}
            for(int i=0;i<c.Meshes.Length;i++)Check(c.Meshes[i],pair.Key+"["+i+"]");
        }
        bool textureAlive=false,materialAlive=false;
        try{textureAlive=Texture!=null && Texture.width>0 && Texture.height>0;if(!textureAlive)errors.Add("texture:destroyed");else if((Texture.hideFlags&HideFlags.DontUnloadUnusedAsset)==0)errors.Add("texture:not-retained");}catch(Exception ex){errors.Add("texture:"+ex.GetType().Name);}
        try{materialAlive=Material!=null && Material.shader!=null && Material.mainTexture==Texture;if(!materialAlive)errors.Add("material:destroyed-or-unbound");else if((Material.hideFlags&HideFlags.DontUnloadUnusedAsset)==0)errors.Add("material:not-retained");}catch(Exception ex){errors.Add("material:"+ex.GetType().Name);}
        if(SourceProfile!=null)InspectSourceMaterials(errors);
        else if(SourceMaterials!=null)
        {
            foreach(var material in SourceMaterials.Append(GhostHiddenMaterial))
                if(material==null || material.shader==null || material.mainTexture!=Texture || (material.hideFlags&HideFlags.DontUnloadUnusedAsset)==0)errors.Add("source-material:destroyed-unbound-or-not-retained");
        }
        return new ModelAssetHealth{Valid=errors.Count==0,Name=Name,SourceVertexCount=VertexCount,ExpectedMeshes=expected,AliveMeshes=alive,RetainedMeshes=retained,ClipCount=Clips.Count,TextureAlive=textureAlive,MaterialAlive=materialAlive,Problems=errors.Take(20).ToArray()};
    }
}
