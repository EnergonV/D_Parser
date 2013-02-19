﻿using System.Collections.Generic;
using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Dom.Statements;
using D_Parser.Parser;
using D_Parser.Resolver;
using D_Parser.Resolver.ASTScanner;
using System.IO;

namespace D_Parser.Completion
{
	public class CtrlSpaceCompletionProvider : AbstractCompletionProvider
	{
		public object parsedBlock;
		public IBlockNode curBlock;
		public ParserTrackerVariables trackVars;

		public CtrlSpaceCompletionProvider(ICompletionDataGenerator cdg) : base(cdg) { }

		protected override void BuildCompletionDataInternal(IEditorData Editor, string EnteredText)
		{
			var visibleMembers = MemberFilter.All;

			IStatement curStmt = null;
			if (curBlock == null)
				curBlock = D_Parser.Resolver.TypeResolution.DResolver.SearchBlockAt(Editor.SyntaxTree, Editor.CaretLocation, out curStmt);

			if (curBlock == null)
				return;

			// 1) Get current context the caret is at
			if (parsedBlock == null)
				parsedBlock = FindCurrentCaretContext(
					Editor.ModuleCode,
					curBlock,
					Editor.CaretOffset,
					Editor.CaretLocation,
					out trackVars);

			// 2) If in declaration and if node identifier is expected, do not show any data
			if (trackVars == null)
			{
				// --> Happens if no actual declaration syntax given --> Show types/keywords anyway
				visibleMembers = MemberFilter.Types | MemberFilter.Keywords | MemberFilter.TypeParameters;

				MemberCompletionEnumeration.EnumAllAvailableMembers(
					CompletionDataGenerator,
					curBlock, 
					null, 
					Editor.CaretLocation, 
					Editor.ParseCache, 
					visibleMembers, 
					new ConditionalCompilationFlags(Editor));
			}
			else
			{
				var n = trackVars.LastParsedObject as INode;
				var dv = n as DVariable;
				if (dv != null && dv.IsAlias && dv.Type == null && trackVars.ExpectingIdentifier)
				{
					// Show completion because no aliased type has been entered yet
				}
				else if (n != null && string.IsNullOrEmpty(n.Name) && trackVars.ExpectingIdentifier)
					return;

				else if (trackVars.LastParsedObject is TokenExpression &&
					DTokens.BasicTypes[(trackVars.LastParsedObject as TokenExpression).Token] &&
					!string.IsNullOrEmpty(EnteredText) &&
					IsIdentifierChar(EnteredText[0]))
					return;

				if (trackVars.LastParsedObject is Modifier)
				{
					var attr = trackVars.LastParsedObject as Modifier;

					if (attr.IsStorageClass && attr.Token != DTokens.Abstract)
						return;
				}

				if ((trackVars.LastParsedObject is NewExpression && trackVars.IsParsingInitializer) ||
					trackVars.LastParsedObject is TemplateInstanceExpression && ((TemplateInstanceExpression)trackVars.LastParsedObject).Arguments == null)
					visibleMembers = MemberFilter.Types;
				else if (EnteredText == " ")
					return;
				// In class bodies, do not show variables
				else if (!(parsedBlock is BlockStatement || trackVars.IsParsingInitializer))
				{
					bool showVariables = false;
					var dbn = parsedBlock as DBlockNode;
					if (dbn != null && dbn.StaticStatements != null && dbn.StaticStatements.Count > 0)
					{
						var ss = dbn.StaticStatements[dbn.StaticStatements.Count -1];
						if (ss.Location <= Editor.CaretLocation && ss.EndLocation <= Editor.CaretLocation)
						{
							showVariables = true;
						}
					}

					if(!showVariables)
						visibleMembers = MemberFilter.Types | MemberFilter.Keywords | MemberFilter.TypeParameters;
				}

				// Hide completion if having typed a '0.' literal
				else if (trackVars.LastParsedObject is IdentifierExpression &&
					   (trackVars.LastParsedObject as IdentifierExpression).Format == LiteralFormat.Scalar)
					return;

				/*
				 * Handle module-scoped things:
				 * When typing a dot without anything following, trigger completion and show types, methods and vars that are located in the module & import scope
				 */
				else if (trackVars.LastParsedObject is TokenExpression &&
					((TokenExpression)trackVars.LastParsedObject).Token == DTokens.Dot)
				{
					visibleMembers = MemberFilter.Methods | MemberFilter.Types | MemberFilter.Variables | MemberFilter.TypeParameters;
					curBlock = Editor.SyntaxTree;
					curStmt = null;
				}

				// In a method, parse from the method's start until the actual caret position to get an updated insight
				if (visibleMembers.HasFlag(MemberFilter.Variables) &&
					curBlock is DMethod &&
					parsedBlock is BlockStatement)
				{
					var bs = parsedBlock as BlockStatement;

					// Insert the updated locals insight.
					// Do not take the caret location anymore because of the limited parsing of our code.
					curStmt = bs.SearchStatementDeeply(bs.EndLocation);

					// now, in most cases, the last inner-most block has been selected.
					// So switch upward by default.
					if (curStmt != null && curStmt.EndLocation == bs.EndLocation &&
						curStmt.Parent != null && curStmt.Parent.EndLocation == bs.EndLocation)
					{
						if (curStmt is BlockStatement)
						{
							/* If we've got an unfinished block, do NOT switch upward in hierarchy
							 * because it's intended to be e.g. in an empty block statement:
							 * for(int k;;)
							 *	|    -- Okay
							 * {
							 *  |	 -- Okay, there's no } at the block end
							 */
							if (Editor.ModuleCode[DocumentHelper.GetOffsetByRelativeLocation(
								Editor.ModuleCode, Editor.CaretLocation, 
								Editor.CaretOffset, bs.EndLocation) - 1] == '}')
									curStmt = curStmt.Parent.Parent;
						}
						else // For non-block statements: Switch only one level up - there's no extra level for e.g. 'for', 'if' or 'foreach'
							curStmt = curStmt.Parent;
					}
				}
				else
					curStmt = null;

				MemberCompletionEnumeration.EnumAllAvailableMembers(
					CompletionDataGenerator,
				    curBlock, 
				    curStmt, 
				    Editor.CaretLocation, 
				    Editor.ParseCache, 
				    visibleMembers, 
				    new ConditionalCompilationFlags(Editor));
			}

			//TODO: Split the keywords into such that are allowed within block statements and non-block statements
			// Insert typable keywords
			if (visibleMembers.HasFlag(MemberFilter.Keywords))
				foreach (var kv in DTokens.Keywords)
					CompletionDataGenerator.Add(kv.Key);

			else if (visibleMembers.HasFlag(MemberFilter.Types))
				foreach (var kv in DTokens.BasicTypes_Array)
					CompletionDataGenerator.Add(kv);
		}

		/// <returns>Either CurrentScope, a BlockStatement object that is associated with the parent method or a complete new DModule object</returns>
		public static object FindCurrentCaretContext(string code,
			IBlockNode CurrentScope,
			int caretOffset, CodeLocation caretLocation,
			out ParserTrackerVariables TrackerVariables)
		{
			bool ParseDecl = false;

			int blockStart = 0;
			var blockStartLocation = CurrentScope != null ? CurrentScope.BlockStartLocation : caretLocation;

			if (CurrentScope is DMethod)
			{
				var block = (CurrentScope as DMethod).GetSubBlockAt(caretLocation);

				if (block != null)
					blockStart = DocumentHelper.GetOffsetByRelativeLocation(code, caretLocation, caretOffset, blockStartLocation = block.Location);
				else
					return FindCurrentCaretContext(code, CurrentScope.Parent as IBlockNode, caretOffset, caretLocation, out TrackerVariables);
			}
			else if (CurrentScope != null)
			{
				if (CurrentScope.BlockStartLocation.IsEmpty || caretLocation < CurrentScope.BlockStartLocation && caretLocation > CurrentScope.Location)
				{
					ParseDecl = true;
					blockStart = DocumentHelper.GetOffsetByRelativeLocation(code, caretLocation, caretOffset, blockStartLocation = CurrentScope.Location);
				}
				else
					blockStart = DocumentHelper.GetOffsetByRelativeLocation(code, caretLocation, caretOffset, CurrentScope.BlockStartLocation);
			}

			if (blockStart >= 0 && caretOffset - blockStart > 0)
				using (var sr = new Misc.StringView(code, blockStart, caretOffset - blockStart))
				{
					var psr = DParser.Create(sr);

					/* Deadly important! For correct resolution behaviour, 
					 * it is required to set the parser virtually to the blockStart position, 
					 * so that everything using the returned object is always related to 
					 * the original code file, not our code extraction!
					 */
					psr.Lexer.SetInitialLocation(blockStartLocation);

					object ret = null;

					if (CurrentScope == null || CurrentScope is IAbstractSyntaxTree)
						ret = psr.Parse();
					else if (CurrentScope is DMethod)
					{
						psr.Step();
						ret = psr.BlockStatement(CurrentScope);
					}
					else if (CurrentScope is DModule)
						ret = psr.Root();
					else
					{
						psr.Step();
						if (ParseDecl)
						{
							var ret2 = psr.Declaration(CurrentScope);

							if (ret2 != null && ret2.Length > 0)
								ret = ret2[0];
						}
						else
						{
							DBlockNode bn = null;
							if (CurrentScope is DClassLike)
							{
								var t = new DClassLike((CurrentScope as DClassLike).ClassType);
								t.AssignFrom(CurrentScope);
								bn = t;
							}
							else if (CurrentScope is DEnum)
							{
								var t = new DEnum();
								t.AssignFrom(CurrentScope);
								bn = t;
							}

							bn.Clear();

							psr.ClassBody(bn);
							ret = bn;
						}
					}

					TrackerVariables = psr.TrackerVariables;
					return ret;
				}

			TrackerVariables = null;

			return null;
		}
	}
}
