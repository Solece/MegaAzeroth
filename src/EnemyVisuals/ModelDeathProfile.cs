using System.Security.Cryptography;
using System.Text.Json;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.EnemyVisuals;

internal sealed partial class Model
{
    internal bool DeathProfileLoaded;
    internal int DeathVariantCount=>Clips.Values.Count(c=>c.AnimationId==1 && c.Name.StartsWith("death",StringComparison.Ordinal));
    internal void LoadDeathProfile(string folder,JsonElement modelJson)
    {
        var path=Path.Combine(folder,"death-profile.json");if(!File.Exists(path))return;
        using var document=JsonDocument.Parse(File.ReadAllText(path));var profile=document.RootElement;
        if(profile.GetProperty("format").GetString()!="azeroth-enemy-death-profile-v1"
            || profile.GetProperty("sourceSha256").GetString()!=modelJson.GetProperty("source").GetProperty("sha256").GetString())
            throw new InvalidDataException("Source enemy death profile/model mismatch: "+Key);
        foreach(var row in profile.GetProperty("animations").EnumerateArray())
        {
            string name=row.GetProperty("name").GetString();
            if(row.GetProperty("animationId").GetInt32()!=1 || !name.StartsWith("death",StringComparison.Ordinal))throw new InvalidDataException("Not an original death sequence: "+Key);
            if(!Clips.TryGetValue(name,out var clip))
            {
                clip=new Clip{Name=name,AnimationId=1,SequenceIndex=row.GetProperty("sequenceIndex").GetInt32(),Duration=row.GetProperty("duration").GetSingle(),Count=row.GetProperty("frameCount").GetInt32()};
                var file=row.GetProperty("file").GetString();
                if(Path.GetFileName(file)!=file)throw new InvalidDataException("Death frame path must be local to source model pack");
                var bytes=File.ReadAllBytes(Path.Combine(folder,file));
                if(bytes.Length!=checked(clip.Count*VertexCount*12) || clip.Count<2 || clip.Duration<=0
                    || Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=row.GetProperty("sha256").GetString())
                    throw new InvalidDataException("Invalid original death variant frames: "+Key+"/"+name);
                clip.Meshes=new Mesh[clip.Count];Clips[name]=clip;
                using var reader=new BinaryReader(new MemoryStream(bytes));
                for(int frame=0;frame<clip.Count;frame++)
                {
                    var vertices=new Vector3[VertexCount];
                    for(int i=0;i<VertexCount;i++)vertices[i]=new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                    var mesh=Retain(Object.Instantiate(Template));clip.Meshes[frame]=mesh;
                    mesh.name=Name+"_"+name+"_"+frame;mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();
                }
            }
            if(clip.SequenceIndex!=row.GetProperty("sequenceIndex").GetInt32())throw new InvalidDataException("Death sequence provenance mismatch: "+Key);
            clip.SourceFrequency=Math.Max(0,row.GetProperty("sourceFrequency").GetInt32());
        }
        DeathProfileLoaded=true;
    }
    internal Clip SelectDeath(uint enemyId)
    {
        var variants=Clips.Values.Where(c=>c.AnimationId==1 && c.Name.StartsWith("death",StringComparison.Ordinal)).OrderBy(c=>c.SequenceIndex).ToArray();
        if(variants.Length==0)return null;if(variants.Length==1)return variants[0];
        int total=variants.Sum(c=>c.SourceFrequency);
        if(total<=0)return variants[0];
        int choice=EnemyDeathPresentation.ChooseVariation(enemyId,total);
        foreach(var clip in variants){if(choice<clip.SourceFrequency)return clip;choice-=clip.SourceFrequency;}
        return variants[^1];
    }
}
