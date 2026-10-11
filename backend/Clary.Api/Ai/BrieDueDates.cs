using System.Globalization;
using System.Text.RegularExpressions;
namespace Clary.Api;

public sealed record BrieDueDateResult(DateOnly? Date,string? Need=null,string? Question=null);

// Calendar interpretation is application-owned. A model never chooses the clock, timezone, year or day.
public static class BrieDueDates {
 static readonly CultureInfo English=CultureInfo.GetCultureInfo("en-US");
 public const string Prompt="What due date should I use? You can say tomorrow, in five days, or October 20.";
 public static BrieDueDateResult Resolve(string? input,string? explicitZone,DateTimeOffset anchor){
  if(string.IsNullOrWhiteSpace(input))return new(null,"dueDate",Prompt);
  var text=Regex.Replace(input.Trim().TrimEnd('.').TrimEnd(),@"(?<=\d)(st|nd|rd|th)\b","",RegexOptions.IgnoreCase);
  text=Regex.Replace(text,@"([A-Za-z])(?=\d)","$1 ");
  if(DateOnly.TryParseExact(text,new[]{"MM-dd-yyyy","yyyy-MM-dd","MMMM d yyyy","MMM d yyyy","MMMM d, yyyy","MMM d, yyyy"},English,DateTimeStyles.AllowWhiteSpaces,out var absolute))return new(absolute);
  var next=Regex.Match(text,@"^next month(?: (?:on )?(\d{1,2}))?$",RegexOptions.IgnoreCase);
  if(next.Success&&!next.Groups[1].Success)return new(null,"dueDay","Which day next month should the ticket be due?");
  var relative=Regex.Match(text,@"^in ([a-z -]+|\d+) days?$",RegexOptions.IgnoreCase);
  int? days=text.Equals("today",StringComparison.OrdinalIgnoreCase)?0:text.Equals("tomorrow",StringComparison.OrdinalIgnoreCase)?1:relative.Success?Number(relative.Groups[1].Value):null;
  var explicitCurrentYear=Regex.IsMatch(text,@"\s+this year$",RegexOptions.IgnoreCase);
  if(explicitCurrentYear)text=Regex.Replace(text,@"\s+this year$","",RegexOptions.IgnoreCase);
  var monthDay=Regex.Match(text,@"^([A-Za-z]+)\s+(\d{1,2})$",RegexOptions.IgnoreCase);
  var month=monthDay.Success?Array.FindIndex(English.DateTimeFormat.MonthNames,x=>x.Equals(monthDay.Groups[1].Value,StringComparison.OrdinalIgnoreCase))+1:0;
  if(month==0&&monthDay.Success)month=Array.FindIndex(English.DateTimeFormat.AbbreviatedMonthNames,x=>x.Equals(monthDay.Groups[1].Value,StringComparison.OrdinalIgnoreCase))+1;
  if(days is null&&!next.Success&&month is <1 or >12)return new(null,"dueDate","I couldn't interpret that date. "+Prompt);
  if(!TryZone(explicitZone,out var zone))return new(null,"timeZone","Which time zone should I use for that date? For example, America/New_York or UTC-04:00. You can also give a date with its year.");
  var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(anchor,zone!).DateTime);
  if(days is int offset){try{return new(today.AddDays(offset));}catch(ArgumentOutOfRangeException){return new(null,"dueDate",Prompt);}}
  if(next.Success){var nextMonth=today.AddMonths(1);var day=int.Parse(next.Groups[1].Value,CultureInfo.InvariantCulture);if(day<1||day>DateTime.DaysInMonth(nextMonth.Year,nextMonth.Month))return new(null,"dueDay","That day isn't in next month. Which day should I use?");return new(new(nextMonth.Year,nextMonth.Month,day));}
  var requestedDay=int.Parse(monthDay.Groups[2].Value,CultureInfo.InvariantCulture);
  if(requestedDay<1||requestedDay>31)return new(null,"dueDate",Prompt);
  if(requestedDay>DateTime.DaysInMonth(today.Year,month))return new(null,"dueYear","Which year do you mean for that date? I won't choose a year or move the day automatically.");
  var candidate=new DateOnly(today.Year,month,requestedDay);
  if(candidate<today&&!explicitCurrentYear)return new(null,"dueYear","That date has already passed this year. Which year do you mean?");
  return new(candidate);
 }
 static int? Number(string text){
  if(int.TryParse(text,out var n))return n is >=0 and <=365?n:null;
  string[] words=["zero","one","two","three","four","five","six","seven","eight","nine","ten","eleven","twelve","thirteen","fourteen","fifteen","sixteen","seventeen","eighteen","nineteen","twenty"];
  n=Array.FindIndex(words,w=>w.Equals(text,StringComparison.OrdinalIgnoreCase));return n>=0?n:null;
 }
 public static bool TryZone(string? input,out TimeZoneInfo? zone){
  zone=null;if(string.IsNullOrWhiteSpace(input))return false;
  var value=input.Trim();var offset=Regex.Match(value,@"^UTC([+-])(\d{1,2})(?::(\d{2}))?$",RegexOptions.IgnoreCase);
  if(offset.Success){var h=int.Parse(offset.Groups[2].Value);var m=offset.Groups[3].Success?int.Parse(offset.Groups[3].Value):0;if(h>14||m>59||(h==14&&m!=0))return false;var span=TimeSpan.FromMinutes((h*60+m)*(offset.Groups[1].Value=="-"?-1:1));zone=TimeZoneInfo.CreateCustomTimeZone(value,span,value,value);return true;}
  try{zone=TimeZoneInfo.FindSystemTimeZoneById(value);return true;}catch(TimeZoneNotFoundException){return false;}catch(InvalidTimeZoneException){return false;}
 }
}

