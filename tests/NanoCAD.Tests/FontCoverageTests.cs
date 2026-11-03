using NanoCAD.Contracts;
using NanoCADBridge;
using System.Text.Json;
internal static class FontCoverageTests {
 public static async Task Run(Func<string,Func<Task>,Task> test) {
  await test("block338 font transition binds explicit null native descriptor",()=>{
   if(TextStyleMetadata.DescribeTypeface(null) is not null||TextStyleMetadata.DescribeTypeface("") is not null||TextStyleMetadata.DescribeTypeface("Arial")!="Arial"||TextStyleMetadata.DescribeTypeface(" ")!=" ")throw new Exception("Read/write font canonical identity disagrees or normalizes a real nonempty name.");
   object Input(object? expected)=>new{operation="set_text_style_font",handle="11",expected_typeface=expected,typeface="Arial",required_text="ABC"};
   if(StyleWriteValidation.ParseFont(Wire.Element(Input(null)),"$").ExpectedTypeface is not null)throw new Exception("Native absent descriptor was invented.");
   foreach(var value in new[]{Wire.Element(Input("")),Wire.Element(new{operation="set_text_style_font",handle="11",typeface="Arial",required_text="ABC"})}){
    try{StyleWriteValidation.ParseFont(value,"$");throw new Exception("Empty or omitted precondition accepted.");}catch(DomainException e)when(e.Error.code=="INVALID_ARGUMENT"){}
   }
   string schema=NanoCAD.McpServer.StyleWriteSchema.SetFont().GetProperty("properties").GetProperty("expected_typeface").GetRawText();
   if(!schema.Contains("null")||!schema.Contains("string"))throw new Exception("Nullable native precondition hidden from discovery.");
   return Task.CompletedTask;
  });
  await test("font coverage rejects known missing Arial diameter",()=>{
   try {GdiFontCoverage.Require("Arial",false,false,"\u2300");throw new Exception("Missing glyph accepted");}
   catch(DomainException e) when(e.Error.code=="FONT_GLYPHS_MISSING"){}
   return Task.CompletedTask;
  });
  await test("font coverage engineering symbols and labels in Segoe UI Symbol",()=>{
   GdiFontCoverage.Require("Segoe UI Symbol",false,false,"6 × ⌀12 PASANTES / 60° ± Ø ABC abc 0123456789");return Task.CompletedTask;
  });
  await test("font coverage rejects substituted missing family",()=>{
   try {GdiFontCoverage.Require("NCMCP_Nonexistent_937",false,false,"ABC");throw new Exception("Substitution accepted");}
   catch(DomainException e) when(e.Error.code=="FONT_UNAVAILABLE"){}
   return Task.CompletedTask;
  });
  await test("font coverage operation schema parses and rejects file path",()=>{
   var input=JsonSerializer.SerializeToElement(new {operation="set_text_style_font",handle="11",expected_typeface="Arial",typeface="Segoe UI Symbol",required_text="⌀±°"});
   var parsed=StyleWriteValidation.ParseFont(input,"$");if(parsed.Typeface!="Segoe UI Symbol")throw new Exception("Wrong family");
   var schema=NanoCAD.McpServer.ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_structure").InputSchema;if(!schema.GetRawText().Contains("required_text"))throw new Exception("Missing published field");
   try {StyleWriteValidation.ParseFont(JsonSerializer.SerializeToElement(new {operation="set_text_style_font",handle="11",expected_typeface="Arial",typeface="C:/font.ttf",required_text="⌀"}),"$");throw new Exception("Path accepted");}
   catch(DomainException e) when(e.Error.code=="INVALID_ARGUMENT"){}
   return Task.CompletedTask;
  });
 }
}
