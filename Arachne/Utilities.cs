using System.Reflection;

namespace Arachne;

internal static class Utilities
{
    public static IEnumerable<Type> FindDerivedTypesInAssembly(Assembly assembly, Type baseType)
    {
        return assembly.GetTypes().Where(t => baseType.IsAssignableFrom(t));
    }

    public static IEnumerable<Type> FindDerivedTypes(Type baseType)
    {
        return AppDomain.CurrentDomain.GetAssemblies().SelectMany(ass =>
        {
            return FindDerivedTypesInAssembly(ass, baseType);
        });
    }

    public static bool IsSubclassOfRawGeneric(Type generic, Type toCheck)
    {
        while (toCheck != null && toCheck != typeof(object))
        {
            var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
            if (generic == cur)
            {
                return true;
            }
            toCheck = toCheck.BaseType!;
        }
        return false;
    }

    public static void WritePackedBool(this BinaryWriter writer, bool b1, bool b2 = false, bool b3 = false, bool b4 = false, bool b5 = false, bool b6 = false, bool b7 = false, bool b8 = false)
    {
        byte b = 0;
        if (b1) b |= 1;
        if (b2) b |= 2;
        if (b3) b |= 4;
        if (b4) b |= 8;
        if (b5) b |= 16;
        if (b6) b |= 32;
        if (b7) b |= 64;
        if (b8) b |= 128;
        writer.Write(b);
    }

    public static (bool, bool, bool, bool, bool, bool, bool, bool) ReadPackedBool(this BinaryReader reader)
    {
        byte b = reader.ReadByte();
        return ((b & 1) != 0, (b & 2) != 0, (b & 4) != 0, (b & 8) != 0, (b & 16) != 0, (b & 32) != 0, (b & 64) != 0, (b & 128) != 0);
    }
}