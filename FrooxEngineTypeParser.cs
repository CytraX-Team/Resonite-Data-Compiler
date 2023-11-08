using System.Reflection;
using System.Text;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;

const char SEPARATOR = '#';

static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
{
    try
    {
        return assembly.GetTypes();
    }
    catch (ReflectionTypeLoadException e)
    {
        return e.Types.Where(t => t != null)!;
    }
}

IEnumerable<Assembly> asms = Directory
    .GetFiles(Directory.GetCurrentDirectory())
    .Where((s) => s.EndsWith(".dll") && !s.StartsWith("System"))
    .Select(
        (s) =>
        {
            try
            {
                return Assembly.LoadFrom(s);
            }
            // For non C# dlls in the managed folder
            catch (BadImageFormatException) { }
            return null;
        }
    )
    .Where((asm) => asm != null)!;

Console.WriteLine($"Loaded {asms.Count()} assemblies.");

List<Type> allTypes = asms.SelectMany(GetLoadableTypes).ToList();

Console.WriteLine($"Loaded {allTypes.Count} types.");

WorkerInitializer.Initialize(allTypes, true);

StringBuilder componentsString = new();

void PrintComp(Type element, StringBuilder builder, int depth, HashSet<string> seenOverloads)
{
    if (typeof(ProtoFluxNode).IsAssignableFrom(element))
    {
        Type toPrint = element;
        /*
        if (ProtoFluxHelper.IsHidden(element)) return;
        string overloadName = ProtoFluxHelper.GetOverloadName(element);
        if (overloadName != null)
        {
            if (seenOverloads.Add(overloadName))
            {
                toPrint = ProtoFluxHelper.GetMatchingOverload(overloadName, null, typeof(ComponentSelector).GetMethod("GetTypeRank", BindingFlags.NonPublic | BindingFlags.Static)?.CreateDelegate<Func<Type, int>>());
            }
            else
            {
                return;
            }
        }
        */
        //builder.AppendLine(new string(SEPARATOR, depth + 1) + " " + ProtoFluxHelper.GetDisplayNode(toPrint).FullName + "@" + toPrint.GetNiceName() + SEPARATOR + toPrint.FullName);
        _ = builder.AppendLine(
            new string(SEPARATOR, depth + 1)
                + " "
                + toPrint.GetNiceName()
                + SEPARATOR
                + toPrint.FullName
        );
        return;
    }
    _ = builder.AppendLine(
        new string(SEPARATOR, depth + 1)
            + " "
            + element.GetNiceName()
            + SEPARATOR
            + element.FullName
    );
}

void ProcessNode(CategoryNode<Type> node, StringBuilder builder, int depth)
{
    _ = builder.AppendLine(new string(SEPARATOR, depth) + " " + node.Name);
    foreach (CategoryNode<Type>? subdir in node.Subcategories)
    {
        ProcessNode(subdir, builder, depth + 1);
    }
    // the line below is only useful in logix mode
    // but commenting it out will cause the PrintComp line to error since it requires that variable
    HashSet<string> seenOverloads = new();
    foreach (Type element in node.Elements)
    {
        PrintComp(element, builder, depth, seenOverloads);
    }
}

foreach (CategoryNode<Type>? node in WorkerInitializer.ComponentLibrary.Subcategories)
{
    if (node.Name == "ProtoFlux")
    {
        continue;
    }

    ProcessNode(node, componentsString, 0);
}

// Writes our components list to a file.
await File.WriteAllTextAsync("../../../../lists/ComponentList.txt", componentsString.ToString());

StringBuilder ProtofluxString = new();
CategoryNode<Type> ProtofluxPath = WorkerInitializer.ComponentLibrary
    .GetSubcategory("ProtoFlux");
    // .GetSubcategory("Runtimes")
    // .GetSubcategory("Execution")
    // .GetSubcategory("Nodes");

foreach (CategoryNode<Type>? node in ProtofluxPath.Subcategories)
{
    ProcessNode(node, ProtofluxString, 0);
}

HashSet<string> seenOverloads = new();
foreach (Type? node in ProtofluxPath.Elements)
{
    PrintComp(node, ProtofluxString, -1, seenOverloads);
}

// Writes our Protoflux list to a file.
await File.WriteAllTextAsync("../../../../lists/ProtoFluxList.txt", ProtofluxString.ToString());
