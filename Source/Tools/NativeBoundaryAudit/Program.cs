using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

// Reads metadata/IL without loading the product or any game/engine assembly.
sealed class TypeNames : ISignatureTypeProvider<string, object>
{
    public string GetArrayType(string t, ArrayShape s) => t + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string t) => t + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => "fn(" + string.Join(",", s.ParameterTypes) + ")->" + s.ReturnType;
    public string GetGenericInstantiation(string t, ImmutableArray<string> a) => t + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object c, int i) => "!" + i;
    public string GetModifiedType(string m, string t, bool r) => t;
    public string GetPinnedType(string t) => t;
    public string GetPointerType(string t) => t + "*";
    public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString();
    public string GetSZArrayType(string t) => t + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
    {
        var t = r.GetTypeDefinition(h); var parent = t.GetDeclaringType();
        return parent.IsNil ? Join(r.GetString(t.Namespace), r.GetString(t.Name)) : GetTypeFromDefinition(r, parent, 0) + "+" + r.GetString(t.Name);
    }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
    {
        var t = r.GetTypeReference(h);
        return t.ResolutionScope.Kind == HandleKind.TypeReference ? GetTypeFromReference(r, (TypeReferenceHandle)t.ResolutionScope, 0) + "+" + r.GetString(t.Name) : Join(r.GetString(t.Namespace), r.GetString(t.Name));
    }
    public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
    static string Join(string a, string b) => a.Length == 0 ? b : a + "." + b;
    public string Name(MetadataReader r, EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(r, (TypeDefinitionHandle)h, 0),
        HandleKind.TypeReference => GetTypeFromReference(r, (TypeReferenceHandle)h, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(r, null, (TypeSpecificationHandle)h, 0),
        _ => h.Kind.ToString()
    };
}

static class Program
{
    sealed record Target(string Type, string Name, string Signature);
    sealed record Source(string File, int Line);
    sealed record Site(string Caller, int Offset, string OpCode, Target Target, Source Source);
    sealed record Declaration(string Kind, string Owner, string Name, string Type, Source Source);
    static readonly TypeNames Names = new();
    static readonly Dictionary<ushort, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => unchecked((ushort)o.Value));
    static readonly string[] GeometryTypes = { "UnityEngine.Vector2", "UnityEngine.Vector3", "UnityEngine.Vector4", "UnityEngine.Rect", "UnityEngine.Color", "UnityEngine.Color32", "UnityEngine.Quaternion", "UnityEngine.Matrix4x4" };
    static bool Geometry(string s) => GeometryTypes.Any(s.Contains);
    static bool Unity(string s) => s.Contains("UnityEngine.");
    static string Signature(MethodSignature<string> s) => s.ReturnType + "(" + string.Join(",", s.ParameterTypes) + ")";
    static Target Resolve(MetadataReader r, EntityHandle h)
    {
        switch (h.Kind)
        {
            case HandleKind.MethodSpecification: return Resolve(r, r.GetMethodSpecification((MethodSpecificationHandle)h).Method);
            case HandleKind.MemberReference:
                var member = r.GetMemberReference((MemberReferenceHandle)h);
                return new(Names.Name(r, member.Parent), r.GetString(member.Name), member.GetKind() == MemberReferenceKind.Method ? Signature(member.DecodeMethodSignature(Names, null)) : member.DecodeFieldSignature(Names, null));
            case HandleKind.MethodDefinition:
                var method = r.GetMethodDefinition((MethodDefinitionHandle)h);
                return new(Names.Name(r, method.GetDeclaringType()), r.GetString(method.Name), Signature(method.DecodeSignature(Names, null)));
            case HandleKind.FieldDefinition:
                var field = r.GetFieldDefinition((FieldDefinitionHandle)h);
                return new(Names.Name(r, field.GetDeclaringType()), r.GetString(field.Name), field.DecodeSignature(Names, null));
            default: return new(Names.Name(r, h), "", "");
        }
    }
    static Source Location(MetadataReader pdb, MethodDefinitionHandle h, int offset)
    {
        if (pdb == null) return null;
        var debug = pdb.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(h)));
        SequencePoint? nearest = null;
        foreach (var point in debug.GetSequencePoints()) if (!point.IsHidden && point.Offset <= offset) nearest = point;
        if (!nearest.HasValue) return null;
        var p = nearest.Value; var doc = p.Document.IsNil ? debug.Document : p.Document;
        return doc.IsNil ? null : new(pdb.GetString(pdb.GetDocument(doc).Name), p.StartLine);
    }
    static int OperandBytes(byte[] il, int at, OperandType kind) => kind switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => checked(4 + 4 * BitConverter.ToInt32(il, at)),
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
        _ => throw new InvalidDataException("Unsupported IL operand " + kind)
    };
    static int Main(string[] args)
    {
        if (args.Length < 2 || args.Length > 3) { Console.Error.WriteLine("Usage: NativeBoundaryAudit PRODUCT.dll OUTPUT.json [PRODUCT.pdb]"); return 2; }
        using var product = File.OpenRead(args[0]); using var pe = new PEReader(product); var r = pe.GetMetadataReader();
        using var symbols = args.Length == 3 ? File.OpenRead(args[2]) : null;
        using var provider = symbols == null ? null : MetadataReaderProvider.FromPortablePdbStream(symbols);
        MetadataReader pdb = provider?.GetMetadataReader();
        // A stale PDB must never attach plausible but incorrect source lines.
        if (pdb != null)
        {
            var expected = new Guid(pdb.DebugMetadataHeader.Id.Take(16).ToArray());
            var entries = pe.ReadDebugDirectory().Where(d => d.Type == DebugDirectoryEntryType.CodeView).ToArray();
            if (entries.Length != 1 || pe.ReadCodeViewDebugDirectoryData(entries[0]).Guid != expected) throw new InvalidDataException("PDB does not match the product's CodeView identity.");
        }
        var calls = new List<Site>(); var fieldUses = new List<Site>(); var tokens = new List<Site>(); var declarations = new List<Declaration>();
        int methods = 0, bodies = 0, instructions = 0;
        foreach (var th in r.TypeDefinitions)
        {
            var type = r.GetTypeDefinition(th); string owner = Names.Name(r, th);
            foreach (var fh in type.GetFields())
            { var f = r.GetFieldDefinition(fh); string t = f.DecodeSignature(Names, null); if (Unity(t)) declarations.Add(new("field", owner, r.GetString(f.Name), t, null)); }
            foreach (var mh in type.GetMethods())
            {
                methods++; var m = r.GetMethodDefinition(mh); string caller = owner + "." + r.GetString(m.Name) + "#" + MetadataTokens.GetRowNumber(mh); var signature = m.DecodeSignature(Names, null);
                if (Unity(Signature(signature))) declarations.Add(new("signature", caller, "", Signature(signature), Location(pdb, mh, 0)));
                if (m.RelativeVirtualAddress == 0) continue;
                bodies++; var body = pe.GetMethodBody(m.RelativeVirtualAddress); var il = body.GetILBytes();
                if (!body.LocalSignature.IsNil)
                {
                    var locals = r.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(Names, null);
                    for (int i = 0; i < locals.Length; i++) if (Unity(locals[i])) declarations.Add(new("local", caller, i.ToString(), locals[i], Location(pdb, mh, 0)));
                }
                for (int i = 0; i < il.Length;)
                {
                    int offset = i; ushort code = il[i++]; if (code == 0xfe) code = (ushort)(0xfe00 | il[i++]); var op = Codes[code]; int bytes = OperandBytes(il, i, op.OperandType);
                    if (i + bytes > il.Length) throw new InvalidDataException("Truncated IL at " + caller + ":" + offset);
                    instructions++;
                    if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok or OperandType.InlineType)
                    {
                        var target = Resolve(r, MetadataTokens.EntityHandle(BitConverter.ToInt32(il, i)));
                        if (Unity(target.Type) || Unity(target.Signature))
                        {
                            var site = new Site(caller, offset, op.Name, target, Location(pdb, mh, offset));
                            if (op.OperandType == OperandType.InlineMethod) calls.Add(site);
                            else if (op.OperandType == OperandType.InlineField) fieldUses.Add(site);
                            else tokens.Add(site);
                        }
                    }
                    i += bytes;
                }
            }
        }
        // These are review candidates, not automatically forbidden APIs: a ctor,
        // callback or XML field can be a necessary current host boundary.
        var geometryCalls = calls.Where(s => Geometry(s.Target.Type) || s.Target.Type is "Verse.Vector3Utility" or "Verse.Vector2Utility").ToArray();
        var report = new { Product = Path.GetFullPath(args[0]), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))), HasMatchingPortablePdb = pdb != null,
            Methods = methods, Bodies = bodies, Instructions = instructions, GeometryCalls = geometryCalls, Calls = calls, FieldUses = fieldUses, TypeTokens = tokens, Declarations = declarations,
            Limits = "Static metadata/IL candidates only. No reflection/string/dynamic/native internals, ownership/control-flow inference or automatic boundary/completion decision. Local source location is the method's first visible sequence point; call sites use nearest preceding visible point." };
        File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { methods, bodies, instructions, calls = calls.Count, geometryCalls = geometryCalls.Length, fieldUses = fieldUses.Count, declarations = declarations.Count }));
        return 0;
    }
}
