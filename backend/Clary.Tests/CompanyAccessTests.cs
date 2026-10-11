using System.Security.Claims;
using Clary.Api.Identity;
using Xunit;

public class CompanyAccessTests
{
    [Fact] public async Task ResolveRequiresActiveUserCompanyAndMembership() => await IdentityFixture.Run(async f=>
    {
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("iss","clary-development"),new Claim("sub","bootstrap")],"test"));
        var access=new CompanyAccess(f.Database);
        var context=await access.ResolveAsync(principal,IdentityFixture.Company,default);
        Assert.NotNull(context);Assert.Equal(CompanyRole.Administrator,context.Role);
        Assert.Null(await access.ResolveAsync(principal,Guid.NewGuid(),default));
        foreach(var table in new[]{"users","companies","memberships"})
        {
            await f.Sql($"UPDATE {table} SET active=false");
            Assert.Null(await access.ResolveAsync(principal,IdentityFixture.Company,default));
            await f.Sql($"UPDATE {table} SET active=true");
        }
        Assert.Null(await access.ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity(principal.Claims)),IdentityFixture.Company,default));
    });
}
