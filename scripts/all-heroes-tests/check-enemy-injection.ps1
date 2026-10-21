param([string]$LabRoot=(Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$ErrorActionPreference='Stop'
$core=Join-Path $LabRoot 'Megabonk-Lab\BepInEx\core'
$dll=Join-Path $LabRoot 'src\EnemyVisuals\bin\Release\net6.0\AzerothMegabonk.EnemyVisuals.dll'
[void][Reflection.Assembly]::LoadFrom((Join-Path $core 'Mono.Cecil.dll'))
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try{
 $types=@('AzerothMegabonk.EnemyVisuals.AzerothVisual','AzerothMegabonk.EnemyVisuals.AzerothCorpseVisual')
 $exports=@();$generated=0
 foreach($name in $types){
  $type=$assembly.MainModule.Types|Where-Object FullName -eq $name
  if(!$type){throw ('Missing injected type '+$name)}
  foreach($method in $type.Methods){
   if($method.IsConstructor){continue}
   $hidden=@($method.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'Il2CppInterop.Runtime.Attributes.HideFromIl2CppAttribute'}).Count -gt 0
   # Some installed interop versions name this attribute without the suffix.
   if(!$hidden){$hidden=@($method.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp'}).Count -gt 0}
   if($method.Name.StartsWith('<')){if(!$hidden){throw ('Generated managed helper exposed to ClassInjector: '+$method.FullName)};$generated++}
   if(!$hidden){
    if($method.IsStatic -or $method.ReturnType.FullName -ne 'System.Void' -or $method.Parameters.Count -ne 0 -or $method.Name -notin @('LateUpdate','OnDestroy')){throw ('Unexpected injected method: '+$method.FullName)}
    $exports+=($type.Name+'.'+$method.Name)
   }
  }
 }
 if($exports.Count -ne 4){throw ('Expected four lifecycle callbacks, observed '+$exports.Count)}
 [pscustomobject]@{pass=$true;exports=$exports;generatedHelpersHidden=$generated;scope='Metadata inspection only; no native game process loaded'}|ConvertTo-Json -Depth 4
}
finally{$assembly.Dispose()}
