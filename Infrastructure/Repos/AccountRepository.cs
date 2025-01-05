﻿using Application.DTOs.Request.Account;
using Application.DTOs.Response;
using Application.DTOs.Response.Account;
using Application.Interfaces;
using Domain.Entities.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Mapster;
using Application.Extensions;

namespace Infrastructure.Repos
{
    public class AccountRepository
        (RoleManager<IdentityRole> roleManager,
         UserManager<ApplicationUser> userManager,IConfiguration config,
         SignInManager<ApplicationUser> signInManager) : IAccount
    {
        private async Task<ApplicationUser> FindUserByEmailAsync(string email)
            => await userManager.FindByEmailAsync(email);

        private async Task<IdentityRole> FindRoleByNameAsync(string roleName)
            => await roleManager.FindByNameAsync(roleName);

        private static string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        public async Task<string> GenerateToken(ApplicationUser user)
        {
            try
            {
                var securitykey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
                var credential = new SigningCredentials(securitykey, SecurityAlgorithms.HmacSha256);

                var userClaims = new[]
                {
                    new Claim(ClaimTypes.Name, user.Email),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, (await userManager.GetRolesAsync(user)).FirstOrDefault().ToString()),
                    new Claim("Fullname", user.Name),
                };

                var token = new JwtSecurityToken(
                    issuer: config["Jwt:Issuer"],
                    audience: config["Jwt:Audience"],
                    claims: userClaims,
                    expires: DateTime.Now.AddMinutes(30),
                    signingCredentials: credential
                    );

                return new JwtSecurityTokenHandler().WriteToken(token);
            }
            catch { return null; }

        }

        private static string CheckResponse(IdentityResult result)
        {
            if (!result.Succeeded)
            {
                var errors = result.Errors.Select(_ => _.Description);
                return string.Join(Environment.NewLine, errors);
            }
            return null;
        }

        public async Task<GeneralResponse> AssignUserToRole(ApplicationUser user, IdentityRole role)
        {
            if (user is null || role is null)
                return new GeneralResponse(false, "Model State cannot be empty");

            var roleExist = await FindRoleByNameAsync(role.Name);
            if (roleExist == null)
                await CreateRoleAsync(role.Adapt(new CreateRoleDTO()));

            IdentityResult result = await userManager.AddToRolesAsync(user, new List<string> {role.Name});

            string error = CheckResponse(result);

            if (!string.IsNullOrEmpty(error))
                return new GeneralResponse(false, error);
            else
                return new GeneralResponse(true, $"{user.Name} assigned to {role.Name} role.");
        } 

        public async Task CreateAdmin()
        {
            try 
            {
                if ((await FindRoleByNameAsync(Constant.Role.Admin)) != null) return;
                var admin = new CreateAccountDTO()
                {
                    Name = "Admin",
                    Password = "Admin@123",
                    EmailAddress = "admin@admin.com",
                    Role = Constant.Role.Admin
                };
                await CreateAccountAsync(admin);
            }
            catch { }
        }

        public async Task<GeneralResponse> CreateAccountAsync(CreateAccountDTO model)
        {
            try 
            {
                if (await FindUserByEmailAsync(model.EmailAddress) != null)
                    return new GeneralResponse(false, "Sorry, user is already created");

                var user = new ApplicationUser()
                {
                    Name= model.Name,
                    UserName = model.EmailAddress,
                    Email = model.EmailAddress,
                    PasswordHash = model.Password
                };

                var result = await userManager.CreateAsync(user, model.Password);
                string error = CheckResponse(result);

                if (!string.IsNullOrEmpty(error))
                    return new GeneralResponse(false, error);

                var (flag, message) = await AssignUserToRole(user, new IdentityRole() { Name = model.Role});
                return new GeneralResponse(flag, message);
            }
            catch(Exception ex)
            {
                return new GeneralResponse(false, ex.Message);
            }
        }

        public Task<GeneralResponse> CreateRoleAsync(CreateRoleDTO model)
        {
            throw new NotImplementedException();
        }

        public Task<GeneralResponse> ChangeRoleAsync()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<GetRoleDTO>> GetRoleAsync()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<GetUserWithRolesDTO>> GetUserWithRoleAsync()
        {
            throw new NotImplementedException();
        }

        public async Task<LoginResponse> LoginAcoountAsync(LoginDto model)
        {
            try 
            {
                var user = await FindUserByEmailAsync(model.EmailAddress);
                if (user == null)
                    return new LoginResponse(false, "User Not Found");

                SignInResult result;
                try
                {
                    result = await signInManager.CheckPasswordSignInAsync(user, model.Password, false);
                }
                catch 
                {
                    return new LoginResponse(false, "Invalid Credential");
                }

                if (!result.Succeeded)
                    return new LoginResponse(false, "Invalid Credential");

                string jwtToken = await GenerateToken(user);
                string refreshToken = GenerateRefreshToken();
                if (string.IsNullOrEmpty(jwtToken) || string.IsNullOrEmpty(refreshToken))
                    return new LoginResponse(false, "Error occured while logging in account, please contact administration");
                else
                    return new LoginResponse(true, $"{user.Name} successfully logged in", jwtToken, refreshToken);
            }
            catch(Exception ex) 
            {
                return new LoginResponse(false, ex.Message);
            }
        }

    }
}
