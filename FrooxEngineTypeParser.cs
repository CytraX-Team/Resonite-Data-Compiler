using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;

internal class Program
{
    private static async Task Main(string[] args)
    {
        const char SEPARATOR = '#';

        HashSet<string> processedAssemblies = [];
        Dictionary<string, string> failedAssemblies = new();
        StringBuilder componentsString = new();

        string GetFrooxEngineDirectory()
        {
            Console.WriteLine("Getting FrooxEngine directory...");

            string? csprojPath = Directory
                .GetFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (csprojPath == null)
            {
                Console.WriteLine("No .csproj file found in the current directory.");
                throw new Exception("No .csproj file found in the current directory.");
            }
            Console.WriteLine($"Found .csproj file at {csprojPath}");

            XDocument doc = XDocument.Load(csprojPath);
            XNamespace? ns = doc.Root?.GetDefaultNamespace();

            if (ns == null)
            {
                Console.WriteLine("No default namespace found in the .csproj file.");
                throw new Exception("No default namespace found in the .csproj file.");
            }

            Console.WriteLine($"Default namespace: {ns}");

            string? resonitePath = doc.Descendants(ns + "ResonitePath")
                .FirstOrDefault(rp => Directory.Exists(rp.Value))
                ?.Value;

            if (resonitePath == null)
            {
                Console.WriteLine("ResonitePath property not found or directory does not exist.");
                throw new Exception("ResonitePath property not found or directory does not exist.");
            }

            IEnumerable<XElement> hintPaths = doc.Descendants(ns + "HintPath")
                .Where(hp => hp.Value.Contains("FrooxEngine.dll"));

            if (!hintPaths.Any())
            {
                Console.WriteLine("FrooxEngine.dll HintPath not found in .csproj file.");
                throw new Exception("FrooxEngine.dll HintPath not found in .csproj file.");
            }

            XElement hintPath = hintPaths.First();

            string expandedHintPath = hintPath.Value.Replace("$(ResonitePath)", resonitePath);
            Console.WriteLine($"FrooxEngine.dll HintPath: {expandedHintPath}");

            string frooxEngineDllPath = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(csprojPath) ?? string.Empty, expandedHintPath)
            );

            Console.WriteLine($"Full path to FrooxEngine.dll: {frooxEngineDllPath}");

            string? frooxEngineDirectory = Path.GetDirectoryName(frooxEngineDllPath);

            if (frooxEngineDirectory == null)
            {
                Console.WriteLine("Failed to get directory name from FrooxEngine.dll path.");
                throw new Exception("Failed to get directory name from FrooxEngine.dll path.");
            }

            Console.WriteLine($"FrooxEngine directory: {frooxEngineDirectory}");

            return frooxEngineDirectory;
        }

        IEnumerable<Type> GetLoadableTypes(Assembly assembly, HashSet<string> processedAssemblies)
        {
            List<Type> types = [];

            // Console.WriteLine($"Loading types from assembly: {assembly.FullName}");

            try
            {
                Type[] exportedTypes = assembly.GetExportedTypes();
                types.AddRange(exportedTypes);
                Console.WriteLine($"Loaded {exportedTypes.Length} types from {assembly.FullName}");
            }
            catch (ReflectionTypeLoadException e)
            {
                // Exclude types that can't be loaded
                IEnumerable<Type> loadableTypes = e.Types.Where(t => t != null).Cast<Type>();
                types.AddRange(loadableTypes);
                Console.WriteLine(
                    $"Loaded {loadableTypes.Count()} types from {assembly.FullName} after excluding unloadable types"
                );
            }
            catch (TypeLoadException e)
            {
                // Skip assembly if a type can't be loaded
                Console.WriteLine(
                    $"Failed to load types from assembly: {assembly.FullName}. Error: {e.Message}"
                );
                return Enumerable.Empty<Type>();
            }
            catch (FileNotFoundException e)
            {
                // Skip assembly if it can't be found
                Console.WriteLine($"Failed to load assembly: {assembly.FullName}. Error: {e.Message}");
                failedAssemblies.Add(string.IsNullOrEmpty(assembly.FullName) ? "DefaultKey" : assembly.FullName, e.Message);
                return Enumerable.Empty<Type>();
            }

            // This idea didn't work, just caused more problems.
            /*
            foreach (AssemblyName referencedAssemblyName in assembly.GetReferencedAssemblies())
            {
                try
                {
                    Assembly referencedAssembly = Assembly.Load(referencedAssemblyName);
                    var referencedTypes = referencedAssembly.GetExportedTypes();
                    // Do something with referencedTypes
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load referenced assembly: {referencedAssemblyName.FullName}. Error: {ex.Message}");
                    failedAssemblies.Add(string.IsNullOrEmpty(referencedAssemblyName.FullName) ? "DefaultKey" : referencedAssemblyName.FullName, e.Message);
                }
            }
            */

            return types;
        }

        IEnumerable<Assembly> asms = Directory
            .GetFiles(GetFrooxEngineDirectory(), "*.dll")
            .Where((s) => !s.StartsWith("System"))
            .Select(
                (s) =>
                {
                    try
                    {
                        // This will throw a BadImageFormatException if s is not a valid assembly.
                        AssemblyName assemblyName = AssemblyName.GetAssemblyName(s);
                        return Assembly.LoadFrom(s);
                    }
                    catch (BadImageFormatException)
                    {
                        Console.WriteLine($"Skipping non-assembly file: {s}");
                        return null;
                    }
                    catch (FileLoadException)
                    {
                        Console.WriteLine($"Skipping unloadable assembly: {s}");
                        return null;
                    }
                }
            )
            .Where((asm) => asm != null)!;

        Console.WriteLine($"Loaded {asms.Count()} assemblies.");

        List<Type> allTypes = asms.SelectMany(asm => GetLoadableTypes(asm, processedAssemblies)).ToList();

        Console.WriteLine($"Loaded {allTypes.Count} types.");

        try
        {
            WorkerInitializer.Initialize(allTypes, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize workers: {ex.Message}");
            return;
        }


        void PrintComp(Type element, StringBuilder builder, int depth, HashSet<string> seenOverloads)
        {
            if (element == null)
            {
                Console.WriteLine("Element is null");
                return;
            }

            if (typeof(ProtoFluxNode).IsAssignableFrom(element))
            {
                Type toPrint = element;
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
            if (node == null)
            {
                Console.WriteLine("Node is null");
                return;
            }

            _ = builder.AppendLine(new string(SEPARATOR, depth) + " " + node.Name);

            foreach (CategoryNode<Type>? subdir in node.Subcategories)
            {
                ProcessNode(subdir, builder, depth + 1);
            }

            HashSet<string> seenOverloads = [];

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

        string outputFolder = args.Length < 1 ? "./data/" : args[0];

        // Get the directory name from the file path
        string directoryName = Path.GetDirectoryName(outputFolder)!;

        // Check if the directory exists, if not, create it
        if (!Directory.Exists(directoryName))
        {
            _ = Directory.CreateDirectory(directoryName);
        }

        async Task ProcessCategoryAndWriteToFile(string? categoryName, string fileName)
        {
            StringBuilder categoryString = new();
            HashSet<string> seenOverloads = [];

            CategoryNode<Type> categoryPath = WorkerInitializer.ComponentLibrary.GetSubcategory(
                categoryName ?? ""
            );

            foreach (CategoryNode<Type>? node in categoryPath.Subcategories)
            {
                if (node.Name != "ProtoFlux")
                {
                    ProcessNode(node, categoryString, 0);
                }
            }

            foreach (Type? node in categoryPath.Elements)
            {
                PrintComp(node, categoryString, -1, seenOverloads);
            }

            // Writes our category list to a file.
            await File.WriteAllTextAsync(Path.Combine(outputFolder, fileName), categoryString.ToString());
        }

        await ProcessCategoryAndWriteToFile(null, "ComponentList.txt");
        await ProcessCategoryAndWriteToFile("ProtoFlux", "ProtoFluxList.txt");

        async Task ProcessAllMethodsAndWriteToFile(string fileName)
        {
            StringBuilder allMethodsString = new();
            Dictionary<string, List<string>> typesToMethods = [];

            foreach (CategoryNode<Type>? node in WorkerInitializer.ComponentLibrary.Subcategories)
            {
                foreach (Type? type in node.Elements)
                {
                    foreach (MethodInfo method in type.GetMethods())
                    {
                        string methodSignature =
                            $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))}){SEPARATOR}{method.ReturnType.Name}";
                        if (!typesToMethods.TryGetValue(type.Name, out List<string>? value))
                        {
                            value = [];
                            typesToMethods[type.Name] = value;
                        }

                        value.Add(methodSignature);
                    }
                }
            }

            foreach (Type? type in WorkerInitializer.ComponentLibrary.Elements)
            {
                foreach (MethodInfo method in type.GetMethods())
                {
                    string methodSignature =
                        $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))}){SEPARATOR}{method.ReturnType.Name}";
                    if (!typesToMethods.TryGetValue(type.Name, out List<string>? value))
                    {
                        value = [];
                        typesToMethods[type.Name] = value;
                    }

                    value.Add(methodSignature);
                }
            }

            // Writes our all methods list to a file.
            foreach (KeyValuePair<string, List<string>> pair in typesToMethods)
            {
                allMethodsString.AppendLine($"{pair.Key}");
                foreach (string methodSignature in pair.Value)
                {
                    allMethodsString.AppendLine($"# {methodSignature}");
                }
            }

            await File.WriteAllTextAsync(Path.Combine(outputFolder, fileName), allMethodsString.ToString());
        }

        await ProcessAllMethodsAndWriteToFile("MethodsList.txt");

        async Task ProcessAllTypesAndWriteToFile(string fileName)
        {
            StringBuilder allTypesString = new();
            HashSet<string> seenTypes = new();

            foreach (CategoryNode<Type> node in WorkerInitializer.ComponentLibrary.Subcategories)
            {
                foreach (Type type in node.Elements)
                {
                    if (type?.FullName != null && seenTypes.Add(type.FullName))
                    {
                        allTypesString.AppendLine(type.FullName);
                    }
                }
            }

            foreach (Type? type in WorkerInitializer.ComponentLibrary.Elements)
            {
                if (type?.FullName != null && seenTypes.Add(type.FullName))
                {
                    allTypesString.AppendLine(type.FullName);
                }
            }

            // Writes our all types list to a file.
            await File.WriteAllTextAsync(Path.Combine(outputFolder, fileName), allTypesString.ToString());
        }

        await ProcessAllTypesAndWriteToFile("TypesList.txt");

        // Output failed assemblies at the end
        if (failedAssemblies.Count != 0)
        {
            Console.WriteLine($"Failed to load the following assemblies: {failedAssemblies.Count}");
            foreach (KeyValuePair<string, string> assembly in failedAssemblies)
            {
                Console.WriteLine($"Assembly: {assembly.Key}, Error: {assembly.Value}");
            }
        }
    }
}