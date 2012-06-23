﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using D_Parser.Dom.Expressions;
using D_Parser.Parser;
using D_Parser.Dom;
using D_Parser.Resolver;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Dom.Statements;
using D_Parser.Resolver.Templates;

namespace D_Parser.Evaluation
{
	public partial class ExpressionEvaluator
	{
		public ISymbolValue Evaluate(TypeidExpression tid)
		{
			/*
			 * Depending on what's given as argument, it's needed to find out what kind of TypeInfo_ class to return
			 * AND to fill it with all required information.
			 * 
			 * http://dlang.org/phobos/object.html#TypeInfo
			 */
			throw new NotImplementedException("TypeInfo creation not supported yet");
		}
	}
}