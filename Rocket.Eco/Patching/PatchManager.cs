﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Rocket.API;
using Rocket.API.DependencyInjection;
using Rocket.API.Logging;
using Rocket.Eco.API;

namespace Rocket.Eco.Patching
{
    public sealed class PatchManager : IPatchManager
    {
        private readonly IDependencyContainer patchContainer;
        private readonly IRuntime runtime;

        public PatchManager(IRuntime runtime)
        {
            this.runtime = runtime;
            patchContainer = runtime.Container.CreateChildContainer();
        }

        public void RegisterPatch(Type type)
        {
            ILogger logger = patchContainer.Get<ILogger>();

            if (!(Activator.CreateInstance(type) is IAssemblyPatch patch)) return;

            patchContainer.RegisterInstance(patch, $"{type.Assembly.FullName}_{patch.TargetAssembly}_{patch.TargetType}");
            logger.LogInformation($"A patch for {patch.TargetType} has been registered.");
        }

        public void RegisterPatch<T>() where T : IAssemblyPatch, new()
        {
            ILogger logger = patchContainer.Get<ILogger>();

            T patch = new T();
            patchContainer.RegisterInstance<IAssemblyPatch>(patch, $"{typeof(T).Assembly.FullName}_{patch.TargetAssembly}_{patch.TargetType}");

            logger.LogInformation($"A patch for {patch.TargetType} has been registered.");
        }

        public void RunPatching()
        {
            if (Assembly.GetCallingAssembly().GetName().Name == "Rocket.Eco")
            {
                Dictionary<string, byte[]> dict = CollectAssemblies();

                string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Rocket", "Binaries", "Eco");
                Directory.CreateDirectory(outputDir);

                foreach (KeyValuePair<string, byte[]> value in dict)
                    File.WriteAllBytes(Path.Combine(outputDir, value.Key), value.Value);

                DefaultAssemblyResolver monoAssemblyResolver = new DefaultAssemblyResolver();
                monoAssemblyResolver.AddSearchDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Rocket", "Binaries", "Eco"));

                PatchAll(dict, patchContainer, monoAssemblyResolver);

                for (int i = 0; i < dict.Values.Count; i++)
                    Assembly.Load(dict.Values.ElementAt(i));
            }
            else
            {
                throw new MethodAccessException("This method may only be called from the Rocket.Eco assembly.");
            }
        }

        private Dictionary<string, byte[]> CollectAssemblies()
        {
            Assembly eco = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name.Equals("EcoServer", StringComparison.InvariantCultureIgnoreCase));

            IEnumerable<string> resources = eco.GetManifestResourceNames().Where(x => x.EndsWith(".compressed", StringComparison.InvariantCultureIgnoreCase)).Where(x => x.StartsWith("costura.", StringComparison.InvariantCultureIgnoreCase));
            Dictionary<string, byte[]> assemblies = new Dictionary<string, byte[]>();

            foreach (string resource in resources)
            {
                string finalName = resource.Replace(".compressed", "").Replace("costura.", "");

                try
                {
                    using (Stream stream = eco.GetManifestResourceStream(resource))
                    {
                        using (DeflateStream deflateStream = new DeflateStream(stream, CompressionMode.Decompress))
                        {
                            WriteAssembly(finalName, deflateStream, assemblies);
                        }
                    }
                }
                catch (Exception e)
                {
                    runtime.Container.Get<ILogger>().LogError("Unable to deflate and write an Assembly to the disk!", e);
                }
            }

            return assemblies;
        }

        private static void PatchAll(IDictionary<string, byte[]> targets, IDependencyResolver resolver, IAssemblyResolver monoCecilResolver)
        {
            IEnumerable<IAssemblyPatch> patches = resolver.GetAll<IAssemblyPatch>();
            foreach (KeyValuePair<string, byte[]> target in targets.ToList())
            {
                string finalName = target.Key;

                IEnumerable<IAssemblyPatch> targetedPatches = patches.Where(x => x.TargetAssembly.Equals(finalName.Replace(".dll", ""), StringComparison.InvariantCultureIgnoreCase));

                if (!targetedPatches.Any())
                    continue;

                using (MemoryStream memStream = new MemoryStream(target.Value))
                {
                    AssemblyDefinition asmDef = AssemblyDefinition.ReadAssembly(memStream, new ReaderParameters
                    {
                        AssemblyResolver = monoCecilResolver
                    });

                    foreach (IAssemblyPatch patch in targetedPatches)
                    {
                        foreach (ModuleDefinition modDef in asmDef.Modules)
                        {
                            TypeDefinition typeDef = modDef.Types.FirstOrDefault(x => x.FullName.Equals(patch.TargetType, StringComparison.InvariantCultureIgnoreCase));

                            if (typeDef == null)
                                continue;

                            patch.Patch(typeDef);

                            break;
                        }
                    }

                    asmDef.Write(memStream);

                    asmDef.Dispose();

                    memStream.Position = 0;
                    WriteAssembly(finalName, memStream, targets);
                }
            }
        }

        private static void WriteAssembly(string finalName, Stream stream, IDictionary<string, byte[]> dict)
        {
            byte[] finalAssembly;

            using (MemoryStream memStream = new MemoryStream())
            {
                byte[] array = new byte[81920];
                int count;

                while ((count = stream.Read(array, 0, array.Length)) != 0)
                    memStream.Write(array, 0, count);

                memStream.Position = 0;

                finalAssembly = new byte[memStream.Length];
                memStream.Read(finalAssembly, 0, finalAssembly.Length);
            }

            dict[finalName] = finalAssembly;
        }
    }

    public interface IPatchManager
    {
        void RegisterPatch<T>() where T : IAssemblyPatch, new();
        void RegisterPatch(Type type);
        void RunPatching();
    }
}