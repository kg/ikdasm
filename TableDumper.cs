//
// Copyright (C) 2011 Xamarin Inc (http://www.xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using IKVM.Reflection;
using IKVM.Reflection.Metadata;

namespace Ildasm
{
	enum MetadataTableIndex {
		CustomAttribute = 0xc,
		ModuleRef = 0x1a,
		Assembly = 0x20,
		AssemblyRef = 0x23,
		ExportedType = 0x27,
	}

	class TableDumper
	{
		Universe universe;
		Assembly assembly;
		Module module;

		public TableDumper (string inputFile) {
            universe = new Universe (UniverseOptions.None);

            var raw = universe.OpenRawModule (System.IO.File.OpenRead (inputFile), System.IO.Path.GetTempPath () + "/Dummy");
            if (raw.IsManifestModule)
            {
                assembly = universe.LoadAssembly (raw);
                module = assembly.ManifestModule;
            }
            else
            {
                var ab = universe.DefineDynamicAssembly (new AssemblyName ("<ModuleContainer>"), IKVM.Reflection.Emit.AssemblyBuilderAccess.ReflectionOnly);
                assembly = ab;
                module = ab.__AddModule (raw);
            }			
		}

		public void DumpTable (TextWriter w, MetadataTableIndex tableIndex) {
			switch (tableIndex) {
			case MetadataTableIndex.Assembly:
				DumpAssemblyTable (w);
				break;
			case MetadataTableIndex.AssemblyRef:
				DumpAssemblyRefTable (w);
				break;
			case MetadataTableIndex.ModuleRef:
				DumpModuleRefTable (w);
				break;
			case MetadataTableIndex.ExportedType:
				DumpExportedTypeTable (w);
				break;
			case MetadataTableIndex.CustomAttribute:
				DumpCustomAttributeTable (w);
				break;
			default:
				throw new NotImplementedException ();
			}
		}

		void HexDump (TextWriter w, byte[] bytes, int len) {
			for (int i = 0; i < len; ++i) {
				if ((i % 16) == 0)
					w.Write (String.Format ("\n0x{0:x08}: ", i));
				w.Write (String.Format ("{0:X02} ", bytes [i]));
			}
		}

		void DumpAssemblyTable (TextWriter w) {
			var t = module.AssemblyTable;
			w.WriteLine ("Assembly Table");
			foreach (var r in t.records) {
				w.WriteLine (String.Format ("Name:          {0}", module.GetString (r.Name)));
				w.WriteLine (String.Format ("Hash Algoritm: 0x{0:x08}", r.HashAlgId));
				w.WriteLine (String.Format ("Version:       {0}.{1}.{2}.{3}", r.MajorVersion, r.MinorVersion, r.BuildNumber, r.RevisionNumber));
				w.WriteLine (String.Format ("Flags:         0x{0:x08}", r.Flags));
				w.WriteLine (String.Format ("PublicKey:     BlobPtr (0x{0:x08})", r.PublicKey));

				var blob = module.GetBlob (r.PublicKey);
				if (blob.Length == 0) {
					w.WriteLine ("\tZero sized public key");
				} else {
					w.Write ("\tDump:");
					byte[] bytes = blob.ReadBytes (blob.Length);
					HexDump (w, bytes, bytes.Length);
					w.WriteLine ();
				}
				w.WriteLine (String.Format ("Culture:       {0}", module.GetString (r.Culture)));
				w.WriteLine ();
			}
		}

		void DumpAssemblyRefTable (TextWriter w) {
			var t = module.AssemblyRef;
			w.WriteLine ("AssemblyRef Table");
			int rowIndex = 1;
			foreach (var r in t.records) {
				w.WriteLine (String.Format ("{0}: Version={1}.{2}.{3}.{4}", rowIndex, r.MajorVersion, r.MinorVersion, r.BuildNumber, r.RevisionNumber));
				w.WriteLine (String.Format ("\tName={0}", module.GetString (r.Name)));
				w.WriteLine (String.Format ("\tFlags=0x{0:x08}", r.Flags));
				var blob = module.GetBlob (r.PublicKeyOrToken);
				if (blob.Length == 0) {
					w.WriteLine ("\tZero sized public key");
				} else {
					w.Write ("\tPublic Key:");
					byte[] bytes = blob.ReadBytes (blob.Length);
					HexDump (w, bytes, bytes.Length);
					w.WriteLine ();
				}
				w.WriteLine ();
				rowIndex ++;
			}
		}

		void DumpModuleRefTable (TextWriter w) {
			var t = module.ModuleRef;
			w.WriteLine ("ModuleRef Table (1.." + t.RowCount + ")");
			int rowIndex = 1;
			foreach (var r in t.records) {
				w.WriteLine (String.Format ("{0}: {1}", rowIndex, module.GetString (r)));
				rowIndex ++;
			}
		}

		string GetManifestImpl (int idx) {
			if (idx == 0)
				return "current module";
			uint table = (uint)idx >> 24;
			uint row = (uint)idx & 0xffffff;
			switch (table) {
			case FileTable.Index:
				return "file " + row;
			case (uint)AssemblyRefTable.Index:
				return "assemblyref " + row;
			case (uint)ExportedTypeTable.Index:
				return "exportedtype " + row;
			default:
				return "";
			}
		}

		void DumpExportedTypeTable (TextWriter w) {
			var t = module.ExportedType;
			w.WriteLine ("ExportedType Table (1.." + t.RowCount + ")");
			int rowIndex = 1;
			foreach (var r in t.records) {
				string name = module.GetString (r.TypeName);
				string nspace = module.GetString (r.TypeNamespace);
				w.WriteLine (String.Format ("{0}: {1}{2}{3} is in {4}, index={5:x}, flags=0x{6:x}", rowIndex, nspace, nspace != "" ? "." : "", name, GetManifestImpl (r.Implementation), r.TypeDefId, r.Flags));
				rowIndex ++;
			}
		}

		string StringifyCattrValue (object val) {
			if (val.GetType () == typeof (string))
				return String.Format ("\"{0}\"", val);
			else if (val == null)
				return "null";
			else
				return val.ToString ();
		}

		void DumpCustomAttributeTable (TextWriter w) {
			var t = module.CustomAttribute;
			w.WriteLine ("CustomAttribute Table (1.." + t.RowCount + ")");
			int rowIndex = 1;
			foreach (var r in t.records) {
			}

			Dictionary<int, string> table_names = new Dictionary<int, string> () {
					{ MethodDefTable.Index, "MethodDef" },
					{ FieldTable.Index,  "FieldDef" },
					{ TypeRefTable.Index, "TypeRef" },
					{ TypeDefTable.Index, "TypeDef" },
					{ ParamTable.Index, "Param" },
					{ InterfaceImplTable.Index, "InterfaceImpl" },
					{ MemberRefTable.Index, "MemberRef" },
					{ AssemblyTable.Index,  "Assembly" },
					{ ModuleTable.Index, "Module" },
					{ PropertyTable.Index, "Property" },
					{ EventTable.Index, "Event" },
					{ StandAloneSigTable.Index, "StandAloneSignature" },
					{ ModuleRefTable.Index, "ModuleRef" },
					{ TypeSpecTable.Index, "TypeSpec" },
					{ AssemblyRefTable.Index, "AssemblyRef" },
					{ FileTable.Index, "File" },
					{ ExportedTypeTable.Index, "ExportedType" },
					{ ManifestResourceTable.Index, "Manifest" },
					{ GenericParamTable.Index, "GenericParam" }
				};

			foreach (var cattr in module.__EnumerateCustomAttributeTable ()) {
				//Console.WriteLine (cattr);

				int parent_token = cattr.__Parent;

				string parent;
				int table_idx = parent_token >> 24;
				int row = parent_token & 0xffffff;
				if (!table_names.TryGetValue (table_idx, out parent))
					parent = "Unknown";

				var args = new StringBuilder ();
				args.Append ("[");
				var sep = "";
				foreach (var arg in cattr.ConstructorArguments) {
					args.Append (sep).Append (StringifyCattrValue (arg.Value));
					sep = ", ";
				}
				foreach (var named_arg in cattr.NamedArguments) {
					args.Append (sep);
					args.Append ("{");
					args.Append (String.Format ("{0} = {1}", named_arg.MemberName, StringifyCattrValue (named_arg.TypedValue.Value)));
					args.Append ("}");
					sep = ", ";
				}
				args.Append ("]");

				var ctor = cattr.Constructor;
				var method = new StringBuilder ();
				method.Append ("instance void class ");
				method.Append (String.Format ("[{0}]{1}", ctor.DeclaringType.Assembly.GetName ().Name, ctor.DeclaringType.ToString ()));
				method.Append ("::'.ctor'(");
				sep = "";
				foreach (var arg in ctor.GetParameters ()) {
					method.Append (sep).Append (arg.ParameterType);
					sep = ", ";
				}
				method.Append (")");

				w.WriteLine (String.Format ("{0}: {1}: {2} {3} {4}", rowIndex, parent, row, method, args));
				rowIndex ++;
			}
		}
	}
}
