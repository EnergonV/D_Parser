﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using D_Parser.Dom.Expressions;
using D_Parser.Resolver;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Evaluation.Exceptions;
using D_Parser.Dom;
using D_Parser.Evaluation.CTFE;

namespace D_Parser.Evaluation
{
	public partial class ExpressionEvaluator
	{
		ISymbolValue EvalId(IExpression idOrTemplateExpression, bool ImplicitlyExecute = true)
		{
			if (vp == null)
				return null;

			AbstractType[] res = null;

			if (idOrTemplateExpression is IdentifierExpression)
				res = ExpressionTypeResolver.Resolve((IdentifierExpression)idOrTemplateExpression, vp.ResolutionContext);
			else if (idOrTemplateExpression is TemplateInstanceExpression)
				res = ExpressionTypeResolver.Resolve((TemplateInstanceExpression)idOrTemplateExpression, vp.ResolutionContext);
			else
				throw new InvalidOperationException("Expression " + idOrTemplateExpression + " not allowed in EvalId");

			if (res == null || res.Length == 0)
			{
				if (idOrTemplateExpression is IdentifierExpression)
					return vp[((IdentifierExpression)idOrTemplateExpression).Value as string];

				return null;
			}
			else if (res.Length > 1)
				throw new EvaluationException(idOrTemplateExpression, "Ambiguous expression", res);

			var r = res[0];

			if (r is MemberSymbol)
			{
				var mr = (MemberSymbol)r;

				// If we've got a function here, execute it
				if (mr.Definition is DMethod)
				{
					if (ImplicitlyExecute)
						return FunctionEvaluation.Execute((DMethod)mr.Definition, null, vp);
					else
						return new InternalOverloadValue(res, idOrTemplateExpression);
				}
				else if (mr.Definition is DVariable)
					return vp[(DVariable)mr.Definition];
			}
			else if (r is UserDefinedType)
				return new TypeValue(r, idOrTemplateExpression);

			return null;
		}
	}
}
