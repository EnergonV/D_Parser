﻿//
// OpDispatchResolution.cs
//
// Author:
//       Alexander Bothe <info@alexanderbothe.com>
//
// Copyright (c) 2013 Alexander Bothe
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using D_Parser.Resolver.ExpressionSemantics;
using D_Parser.Dom;

namespace D_Parser.Resolver.TypeResolution
{
	static class OpDispatchResolution
	{
		public static readonly int opDispatchId = "opDispatch".GetHashCode();
		/// <summary>
		/// http://dlang.org/operatoroverloading.html#Dispatch
		/// Check for the existence of an opDispatch overload.
		/// Important: Because static opDispatches are allowed as well, do check whether we can access non-static overloads from non-instance expressions or such
		/// </summary>
		public static IEnumerable<AbstractType> TryResolveFurtherIdViaOpDispatch (ResolutionContext ctxt, int nextIdentifierHash, UserDefinedType b)
		{
			// The usual SO prevention
			if (nextIdentifierHash == opDispatchId || b == null)
				yield break;
				
			var pop = ctxt.ScopedBlock != b.Definition;
			if (pop) {
				// Mainly required for not resolving opDispatch's return type, as this will be performed later on in higher levels
				var opt = ctxt.CurrentContext.ContextDependentOptions;
				ctxt.PushNewScope (b.Definition as IBlockNode);
				ctxt.CurrentContext.IntroduceTemplateParameterTypes (b);
				ctxt.CurrentContext.ContextDependentOptions = opt;
			}

			// Look for opDispatch-Members inside b's Definition
			var overloads = TypeDeclarationResolver.ResolveFurtherTypeIdentifier (opDispatchId, new[]{b}, ctxt);

			if(pop)
				ctxt.Pop ();

			if (overloads == null || overloads.Length < 0)
				yield break;

			var av = new ArrayValue (Evaluation.GetStringType(ctxt), Strings.TryGet(nextIdentifierHash));

			foreach (DSymbol o in overloads) {
				var dn = o.Definition;
				if (dn.TemplateParameters != null && dn.TemplateParameters.Length > 0 && 
					dn.TemplateParameters[0] is TemplateValueParameter)
				{
					//TODO: Test parameter types for being a string value
					o.DeducedTypes = new System.Collections.ObjectModel.ReadOnlyCollection<TemplateParameterSymbol> ( 
						new[]{ new TemplateParameterSymbol(dn.TemplateParameters[0], av) } );
					yield return o;
				}
			}
		}
	}
}

