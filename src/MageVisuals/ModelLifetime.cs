using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.MageVisuals;

internal sealed class ModelAssetHealth
{
    public bool Valid {get;set;}
    public string Folder {get;set;}
    public int SourceVertexCount {get;set;}
    public int ExpectedMeshes {get;set;}
    public int CheckedMeshes {get;set;}
    public int AliveMeshes {get;set;}
    public int RetainedMeshes {get;set;}
    public int ClipCount {get;set;}
    public bool TextureAlive {get;set;}
    public bool MaterialAlive {get;set;}
    public bool HeadClosed {get;set;}
    public string[] Problems {get;set;}
}

internal sealed partial class Model
{
    internal string SourceFolder="";
    internal static T Retain<T>(T asset) where T:Object
    {
        // A managed/IL2CPP wrapper in a C# dictionary is not a Unity native root.
        // Inactive baked animation frames must survive native stage cleanup.
        if(asset!=null)asset.hideFlags|=HideFlags.DontUnloadUnusedAsset;
        return asset;
    }
    internal bool IsUsable(bool allFrames=true)=>Inspect(allFrames).Valid;
    internal ModelAssetHealth Inspect(bool allFrames=true)
    {
        var problems=new List<string>();int expected=1+Clips.Values.Sum(c=>c.Count),checkedMeshes=0,alive=0,retained=0;
        void CheckMesh(Mesh mesh,string label)
        {
            checkedMeshes++;
            try
            {
                if(mesh==null){problems.Add(label+":destroyed");return;}
                int count=mesh.vertexCount;alive++;
                if(count!=VertexCount)problems.Add(label+":vertexCount="+count);
                if((mesh.hideFlags&HideFlags.DontUnloadUnusedAsset)!=0)retained++;else problems.Add(label+":not-retained");
            }
            catch(Exception ex){problems.Add(label+":"+ex.GetType().Name);}
        }
        CheckMesh(Template,"template");
        if(allFrames)foreach(var pair in Clips)
        {
            var c=pair.Value;if(c.Meshes==null || c.Meshes.Length!=c.Count){problems.Add(pair.Key+":frame-array");continue;}
            for(int i=0;i<c.Meshes.Length;i++)CheckMesh(c.Meshes[i],pair.Key+"["+i+"]");
        }
        bool textureAlive=false,materialAlive=false;
        try{textureAlive=Texture!=null && Texture.width>0 && Texture.height>0;if(!textureAlive)problems.Add("texture:destroyed");else if((Texture.hideFlags&HideFlags.DontUnloadUnusedAsset)==0)problems.Add("texture:not-retained");}catch(Exception ex){problems.Add("texture:"+ex.GetType().Name);}
        try{materialAlive=Material!=null && Material.shader!=null && Material.mainTexture==Texture;if(!materialAlive)problems.Add("material:destroyed-or-unbound");else if((Material.hideFlags&HideFlags.DontUnloadUnusedAsset)==0)problems.Add("material:not-retained");}catch(Exception ex){problems.Add("material:"+ex.GetType().Name);}
        if(SourceMaterials!=null)foreach(var material in SourceMaterials)if(material==null||material.shader==null||material.mainTexture!=Texture||(material.hideFlags&HideFlags.DontUnloadUnusedAsset)==0)problems.Add("source-material:destroyed-unbound-or-not-retained");
        return new ModelAssetHealth{Valid=problems.Count==0,Folder=SourceFolder,SourceVertexCount=VertexCount,ExpectedMeshes=expected,CheckedMeshes=checkedMeshes,AliveMeshes=alive,RetainedMeshes=retained,ClipCount=Clips.Count,TextureAlive=textureAlive,MaterialAlive=materialAlive,HeadClosed=HeadClosed,Problems=problems.Take(20).ToArray()};
    }
}
