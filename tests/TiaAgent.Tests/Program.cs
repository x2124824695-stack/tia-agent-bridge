using TiaAgent.Contracts;
using TiaAgent.Host.Safety;

var count = 0;
void Check(bool valid, string name) { if (!valid) throw new Exception(name); count++; }
void Reject(Action action, string name)
{
    try { action(); } catch (ArgumentException) { count++; return; } catch (InvalidOperationException) { count++; return; }
    throw new Exception("Should reject: " + name);
}
SourceValidation.SingleDeclaration("FUNCTION_BLOCK \"轴控制\"\nBEGIN\nEND_FUNCTION_BLOCK", "轴控制", "scl"); count++;
SourceValidation.SingleDeclaration("(* nested (* TYPE Fake *) comment *)\nTYPE \"状态\"\nSTRUCT\nx:Bool;\nEND_STRUCT;\nEND_TYPE", "状态", "udt"); count++;
Reject(() => SourceValidation.SingleDeclaration("// FUNCTION_BLOCK Target\nFUNCTION_BLOCK Other\nEND_FUNCTION_BLOCK", "Target", "scl"), "name only in comment");
Reject(() => SourceValidation.SingleDeclaration("FUNCTION_BLOCK Other\nBEGIN\nx := 'Target';\nEND_FUNCTION_BLOCK", "Target", "scl"), "name only in string");
Reject(() => SourceValidation.SingleDeclaration("FUNCTION_BLOCK Target\nEND_FUNCTION_BLOCK\nFUNCTION_BLOCK Other\nEND_FUNCTION_BLOCK", "Target", "scl"), "multiple objects");
Reject(() => SourceValidation.SingleDeclaration("FUNCTION_BLOCK Target\nEND_FUNCTION_BLOCK FUNCTION_BLOCK Other\nEND_FUNCTION_BLOCK", "Target", "scl"), "same-line second object");
Reject(() => SourceValidation.SingleDeclaration("DATA_BLOCK Target\nEND_DATA_BLOCK", "Target", "scl"), "wrong kind");
Reject(() => SourceValidation.SingleDeclaration("FUNCTION Target\nEND_FUNCTION", "Target", "scl", "FUNCTION_BLOCK"), "cannot change existing FB into FC");
Reject(() => SourceValidation.SingleDeclaration("(* unfinished\nFUNCTION_BLOCK Target", "Target", "scl"), "unterminated comment");
var xml = "<Document><DocumentInfo><Created>now</Created></DocumentInfo><SW.Blocks.FB><AttributeList><Name>Target</Name></AttributeList></SW.Blocks.FB></Document>";
SourceValidation.SingleXmlObject(xml, "Target", "Blocks"); count++;
Reject(() => SourceValidation.SingleXmlObject(xml, "Other", "Blocks"), "XML name");
Reject(() => SourceValidation.SingleXmlObject(xml, "Target", "Types"), "XML kind");
Reject(() => SourceValidation.SingleXmlObject(xml.Replace("</Document>", "<SW.Blocks.FC/></Document>"), "Target", "Blocks"), "multiple XML objects");
try { SourceValidation.ParseXml("<!DOCTYPE Document [<!ENTITY x SYSTEM 'file:///secret'>]><Document>&x;</Document>"); throw new Exception("XXE accepted"); } catch (System.Xml.XmlException) { count++; }
Check(SourceValidation.CanonicalXml(xml) == SourceValidation.CanonicalXml(xml.Replace("now", "later")), "volatile document metadata");
Check(!CompileOutcome.Succeeded(new CompileReportDto { State = "Error", ErrorCount = 0 }), "error state with zero count");
Check(!CompileOutcome.Succeeded(new CompileReportDto { State = "Unknown" }), "unknown state");
Check(CompileOutcome.Succeeded(new CompileReportDto { State = "Warning", WarningCount = 1 }), "warning-only report");
Check(!CompileOutcome.Succeeded(new CompileReportDto { State = "Success", ErrorCount = 1 }), "inconsistent report");
var tokens = new PreviewTokenService();
var token = tokens.Create("projectA|PLC/Blocks/FB", "before", "after");
Reject(() => tokens.Consume(token.Value, "projectB|PLC/Blocks/FB", "before", "after"), "project token binding");
token = tokens.Create("projectA|PLC/Blocks/FB", "before", "after");
tokens.Consume(token.Value, "projectA|PLC/Blocks/FB", "before", "after"); count++;
Reject(() => tokens.Consume(token.Value, "projectA|PLC/Blocks/FB", "before", "after"), "token replay");
Console.WriteLine($"PASS: {count} source/XML/compile/token checks.");
