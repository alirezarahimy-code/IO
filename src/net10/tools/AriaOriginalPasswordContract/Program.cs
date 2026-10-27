using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

// Windows/.NET Framework x86 metadata-only inspector of the pinned TWF 4.28
// Personnel.dll. NEVER calls CheckUserPass, opens SQL, reads a password, or
// prints ldstr, connection strings, embedded keys, IL bytes or customer data.
// Its result is a call graph, not proof of actual credential semantics.
internal static class Program
{
    private static readonly Dictionary<ushort, OpCode> Codes = new Dictionary<ushort, OpCode>();

    private static int Main(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException("Pass the original Personnel.dll path only.");
        string bin = Path.GetDirectoryName(Path.GetFullPath(args[0]));
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string file = Path.Combine(bin, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            OpCode opcode = (OpCode)field.GetValue(null);
            Codes[(ushort)opcode.Value] = opcode;
        }
        Assembly original = Assembly.LoadFrom(args[0]);
        if (original.GetName().Name != "Personnel")
            throw new InvalidOperationException("Expected original TWF 4.28 Personnel.dll.");
        // `personnelSystem.DB.UserInfos` in the original C# is a PROPERTY CHAIN,
        // not a promised fully qualified type name. Discover the actual type
        // and overload from this DLL's metadata, never from the current rewrite.
        Type[] types;
        try { types = original.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
        MethodInfo[] candidates = types.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static).Where(m => m.Name == "CheckUserPass")).ToArray();
        foreach (MethodInfo candidate in candidates)
            Console.WriteLine("OVERLOAD " + SafeName(candidate) + "(" +
                string.Join(",", candidate.GetParameters().Select(p => p.ParameterType.Name).ToArray()) +
                ") -> " + candidate.ReturnType.Name);
        MethodInfo target = candidates.Single(m =>
        {
            ParameterInfo[] p = m.GetParameters();
            return p.Length == 3 && p[0].ParameterType == typeof(string) &&
                p[1].ParameterType == typeof(string) && p[2].ParameterType == typeof(bool) &&
                (m.ReturnType == typeof(long) || m.ReturnType == typeof(int));
        });
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Tuple<MethodBase, int>>();
        pending.Enqueue(Tuple.Create((MethodBase)target, 0));
        while (pending.Count != 0)
        {
            Tuple<MethodBase, int> entry = pending.Dequeue();
            MethodBase current = entry.Item1;
            string id = current.Module.ModuleVersionId.ToString("N") + ":" + current.MetadataToken;
            if (!visited.Add(id)) continue;
            if (visited.Count > 16)
                throw new InvalidOperationException("Unexpectedly large original credential call graph; inspect locally.");
            Console.WriteLine("METHOD depth=" + entry.Item2 + " " + SafeName(current));
            foreach (MethodBase called in DirectCalls(current))
            {
                Console.WriteLine("  CALL " + SafeName(called));
                if (entry.Item2 < 2 && called.DeclaringType != null &&
                    called.DeclaringType.Assembly == original && called.GetMethodBody() != null)
                    pending.Enqueue(Tuple.Create(called, entry.Item2 + 1));
            }
        }
        Console.WriteLine("Summary: original Personnel.CheckUserPass call metadata inspected; NO passwords, SQL or runtime behavior tested");
        return 0;
    }

    private static string SafeName(MethodBase method)
    {
        Type type = method.DeclaringType;
        return (type == null ? "<unknown>" : type.FullName) + "." + method.Name;
    }

    private static IEnumerable<MethodBase> DirectCalls(MethodBase method)
    {
        MethodBody body = method.GetMethodBody();
        if (body == null) yield break;
        byte[] il = body.GetILAsByteArray();
        int p = 0;
        while (p < il.Length)
        {
            ushort key = il[p++];
            if (key == 0xfe)
            {
                if (p >= il.Length) throw new BadImageFormatException("Incomplete IL opcode.");
                key = (ushort)(0xfe00 | il[p++]);
            }
            OpCode opcode;
            if (!Codes.TryGetValue(key, out opcode))
                throw new BadImageFormatException("Unknown IL opcode (no byte dump).");
            int size = 0;
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch:
                    if (p + 4 > il.Length) throw new BadImageFormatException("Truncated IL switch.");
                    int count = BitConverter.ToInt32(il, p);
                    if (count < 0 || count > (il.Length - p - 4) / 4)
                        throw new BadImageFormatException("Invalid IL switch (no byte dump).");
                    size = 4 + 4 * count;
                    break;
                default: size = 4; break;
            }
            if (p + size > il.Length) throw new BadImageFormatException("Truncated IL operand.");
            if ((opcode == OpCodes.Call || opcode == OpCodes.Callvirt || opcode == OpCodes.Newobj) && size == 4)
            {
                int token = BitConverter.ToInt32(il, p);
                MethodBase resolved;
                try
                {
                    Type[] typeArgs = method.DeclaringType == null || !method.DeclaringType.IsGenericType
                        ? Type.EmptyTypes : method.DeclaringType.GetGenericArguments();
                    Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
                    resolved = method.Module.ResolveMethod(token, typeArgs, methodArgs);
                }
                catch (Exception e)
                {
                    if (e is ArgumentException || e is BadImageFormatException ||
                        e is FileNotFoundException || e is TypeLoadException || e is NotSupportedException)
                    {
                        Console.WriteLine("  CALL <unresolved metadata method, no token or string printed>");
                        resolved = null;
                    }
                    else throw;
                }
                if (resolved != null) yield return resolved;
            }
            // InlineString tokens are NEVER resolved or printed.
            p += size;
        }
    }
}
