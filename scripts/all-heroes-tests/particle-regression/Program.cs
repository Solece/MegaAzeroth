using AzerothAbilities;
using System.Text.Json;

int checks=0;
void Assert(bool value,string label){checks++;if(!value)throw new Exception(label);}
void Near(float actual,float expected,string label,float tolerance=.00001f)=>Assert(MathF.Abs(actual-expected)<=tolerance,label+": "+actual+" vs "+expected);
void Report(object result)
{
    string json=JsonSerializer.Serialize(result);Console.WriteLine(json);
    if(args.Length>0)try{File.WriteAllText(args[0],json+Environment.NewLine);}catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
}
try
{
    for(int i=0;i<1024;i++)
    {
        float u=(i+.5f)/1024,v=((i*613)%1024+.5f)/1024;
        OriginalParticleMath.Sphere(0,MathF.PI,u,v,out float x,out float y,out float z);
        Near(y,0,"zero vertical remains flat");Near(x*x+y*y+z*z,1,"unit direction");
        float radius=OriginalParticleMath.SphereRadius(.8333333f,.8333333f,u);
        Near(radius,.8333333f,"equal radii yield original sphere ring");Near(y*radius,0,"flat birth center");
        OriginalParticleMath.Sphere(.9f,1.3f,u,v,out x,out y,out z);
        Near(x*x+y*y+z*z,1,"general sphere normalized");
        OriginalParticleMath.Sphere(.9f,1.3f,1-u,1-v,out float mx,out float my,out float mz);
        Near(mx,x,"symmetric x");Near(my,-y,"symmetric y");Near(mz,-z,"symmetric z");
        Near(OriginalParticleMath.SphereRadius(2,1,u),2,"reversed radius bounds source max0");
        float varying=OriginalParticleMath.SphereRadius(1,2,u);Assert(varying>=1&&varying<=2,"sphere offset radius bounded");
    }
    OriginalParticleMath.Sphere(0,MathF.PI,.5f,.7f,out float rx,out float ry,out float rz);
    OriginalParticleMath.SphereVelocity(rx,ry,rz,rx*.8333333f,0,rz*.8333333f,0,0x8009,out float vx,out float vy,out float vz);
    Near(vx,rx,"Consecration0x8009 radial x invariant");Near(vy,0,"Consecration0x8009 remains flat");Near(vz,rz,"Consecration0x8009 radial z invariant");
    foreach(uint flags in new uint[]{8969,8457,297,313,257,16681,17193})
    {
        OriginalParticleMath.SphereVelocity(rx,ry,rz,rx,0,rz,0,flags,out vx,out vy,out vz);
        Near(vx,0,"real trap/shield source flag up x");Near(vy,1,"real trap/shield source flag up y");Near(vz,0,"real trap/shield source flag up z");
    }
    OriginalParticleMath.SphereVelocity(1,0,0,3,0,0,-4,0x100,out vx,out vy,out vz);
    Near(vx,.6f,"nonzero source origin overrides up flag x");Near(vy,.8f,"nonzero source origin overrides up flag y");Near(vz,0,"nonzero source origin z");
    OriginalParticleMath.SphereVelocity(1,0,0,0,1,0,1,0,out vx,out vy,out vz);
    Near(vx,0,"coincident origin finite fallback x");Near(vy,1,"coincident origin finite fallback up");Near(vz,0,"coincident origin finite fallback z");
    OriginalParticleMath.SphereVelocity(1,0,0,float.MaxValue,1,0,1,0,out vx,out vy,out vz);
    Near(vx,0,"overflow normalize finite fallback x");Near(vy,1,"overflow normalize finite fallback up");Near(vz,0,"overflow normalize finite fallback z");
    var e=new OriginalParticleEmission();
    Assert(e.Take(0,.033f,true,0,256)==0&&!e.BurstLatched,"zero source rate does not consume burst");
    Assert(e.Take(200,0,true,0,256)==0&&!e.BurstLatched,"paused clock does not trigger burst");
    Assert(e.Take(200,.033f,true,0,256)==200,"source Consecration200 admitted");
    Assert(e.TotalSpawned==200&&e.Bursts==1&&e.Clipped==0,"actual counters reflect full source burst");
    for(int i=0;i<100;i++)Assert(e.Take(200,.033f,true,i<40?200:0,256)==0,"positive rate does not repeat after TTL");
    Assert(e.TotalSpawned==200&&e.Bursts==1,"late after1.5 remains one burst");
    e.Take(0,0,true,0,256);Assert(e.BurstLatched,"paused zero sample does not rearm");
    e.Take(0,.033f,true,0,256);Assert(!e.BurstLatched,"actual zero rate rearms");
    Assert(e.Take(.1f,.033f,true,0,256)==0&&e.Bursts==2,"subunit positive count uses actual source truncf");
    Assert(e.BurstLatched&&e.Take(.9f,.033f,true,0,256)==0,"positive subunit edge still latches without retry");
    var clipped=new OriginalParticleEmission();
    Assert(clipped.Take(300,.033f,true,0,256)==256,"burst capacity remains bounded256");
    Assert(clipped.Clipped==44&&clipped.LastClipped==44,"oversized burst clipping explicit");
    Assert(clipped.Take(300,.033f,true,0,256)==0,"clipped burst never silently retries");
    var occupied=new OriginalParticleEmission();Assert(occupied.Take(20,.033f,true,250,256)==6&&occupied.Clipped==14,"occupied capacity reported");
    var continuous=new OriginalParticleEmission();
    Assert(continuous.Take(1600,1,false,0,192)==192,"continuous legacy capacity192");
    Assert(continuous.Budget==1&&continuous.Clipped==1407,"continuous legacy one carry preserved");
    Assert(continuous.Take(0,0,false,0,192)==0&&continuous.Budget==1,"pause preserves pending continuous budget");
    Assert(continuous.Take(0,.033f,false,0,192)==1&&continuous.Budget==0,"continuous pending carry resumes");
    Report(new{Passed=true,Checks=checks,Scope="Actual extracted production math/emission helper. No Unity/native renderer or visual-equivalence assertion."});
}
catch(Exception ex){Report(new{Passed=false,Checks=checks,Error=ex.ToString()});Environment.ExitCode=1;}
