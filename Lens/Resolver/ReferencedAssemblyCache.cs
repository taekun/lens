﻿using System;
using System.Collections.Generic;
using System.Reflection;

namespace Lens.Resolver
{
	/// <summary>
	/// The internal list of assemblies referenced by the current script.
	/// </summary>
	internal class ReferencedAssemblyCache
	{
		#region Constructor

		public ReferencedAssemblyCache(bool useDefault = true)
		{
			_Assemblies = new HashSet<Assembly>();

			if (useDefault)
			{
				foreach (var name in DefaultAssemblyFullNames)
				{
					try
					{
						_Assemblies.Add(Assembly.Load(name));
					}
					catch { }
				}

				foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
					_Assemblies.Add(asm);
			}
		}

		#endregion

		#region Fields

		/// <summary>
		/// Full names of assemblies referenced by the script by default.
		/// </summary>
		private readonly static string[] DefaultAssemblyFullNames =
		{
			"mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
			"System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
			"System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
		};

		/// <summary>
		/// The unique list of referenced assemblies.
		/// </summary>
		private readonly HashSet<Assembly> _Assemblies;

		/// <summary>
		/// List of assemblies that can be used by type or extension method resolvers.
		/// </summary>
		public IEnumerable<Assembly> Assemblies
		{
			get
			{
				// return independent enumerable that cannot be cast to the original HashSet for manipulations
				foreach (var curr in _Assemblies)
					yield return curr;
			}
		}

		#endregion

		#region Methods

		/// <summary>
		/// Register a new assembly as referenced.
		/// </summary>
		public void ReferenceAssembly(Assembly asm)
		{
			_Assemblies.Add(asm);
		}

		#endregion
	}
}
