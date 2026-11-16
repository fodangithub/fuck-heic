using System;using System.Linq;using System.Reflection;
var asm = Assembly.LoadFrom(@"C:\Users\DigitalCrab\.nuget\packages\libheifsharp\3.2.0\lib\netstandard2.1\LibHeifSharp.dll");
var t = asm.GetTypes().First(x=>x.Name=="HeifContext");
Console.WriteLine("=== HeifContext constructors ===");
foreach(var c in t.GetConstructors())
  Console.WriteLine($"  ({string.Join(", ",c.GetParameters().Select(p=>p.ParameterType.Name+" "+p.Name))})");
