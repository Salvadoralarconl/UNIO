using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
namespace Clary.Api.LocalIdentity;
public sealed class LocalPasswordService
{
 private readonly PasswordHasher<object> hasher=new(Options.Create(new PasswordHasherOptions{CompatibilityMode=PasswordHasherCompatibilityMode.IdentityV3,IterationCount=220000}));
 private readonly object user=new();
 public bool Validate(string password,string confirmation)=>password is {Length: >=15 and <=128} && string.Equals(password,confirmation,StringComparison.Ordinal);
 public string Hash(string password)
 {
  if(!Validate(password,password))throw new ArgumentException("Password must contain 15 to 128 characters.");
  return hasher.HashPassword(user,password);
 }
 public PasswordVerificationResult Verify(string hash,string password)
 {
  if(string.IsNullOrEmpty(hash)||hash.Length>4096||password is null||password.Length>128)return PasswordVerificationResult.Failed;
  try{return hasher.VerifyHashedPassword(user,hash,password);}
  catch(Exception ex) when(ex is FormatException or ArgumentException or OverflowException or IndexOutOfRangeException){return PasswordVerificationResult.Failed;}
 }
}
