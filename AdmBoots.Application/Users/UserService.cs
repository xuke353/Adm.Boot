﻿using AdmBoots.Application.Users.Dto;
using System;
using System.Collections.Generic;
using AdmBoots.Domain.Models;
using AdmBoots.Domain.IRepositories;
using System.Linq;
using System.Threading.Tasks;
using AdmBoots.Infrastructure.CustomExceptions;
using AdmBoots.Data.EntityFrameworkCore.Uow;
using AdmBoots.Domain;
using AdmBoots.Infrastructure.Helper;
using AdmBoots.Infrastructure.Framework.Abstractions;
using Microsoft.EntityFrameworkCore;
using AdmBoots.Infrastructure.Extensions;

namespace AdmBoots.Application.Users {

    internal class UserService : AppServiceBase, IUserService {
        private readonly IRepository<User, int> _userRepository;
        private readonly IRepository<Role, int> _roleRepository;
        private readonly IRepository<UserRole, int> _userRoleRepository;

        public UserService(IRepository<User, int> userRepository, IRepository<Role, int> roleRepository, IRepository<UserRole, int> userRoleRepository) {
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _userRoleRepository = userRoleRepository;
        }
        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public async Task<LoginUserInfo> LonginAsync(LoginInput input) {
            var md5Pwd = MD5Helper.MD5Encrypt32(input.Password);
            var users = await _userRepository.GetAllListAsync(u => u.UserName == input.UserName && u.Password == md5Pwd && u.Status == SysStatus.有效);
            if (users.Count > 0) {
                var user = users.FirstOrDefault();
                user.LastLoginTime = DateTime.Now;
                _userRepository.Update(user);

                var userInfo = ObjectMapper.Map<LoginUserInfo>(user);
                userInfo.Roles = userInfo.Roles = GetUserRoles(user.Id); ;
                return userInfo;
            }
            return null;
        }
        [UnitOfWork(IsDisabled = true)]
        public async Task<LoginUserInfo> GetLoginUserAsync(int id) {
            var user = await _userRepository.FirstOrDefaultAsync(t => t.Id == id);
            if (user != null) {
                var userInfo = ObjectMapper.Map<LoginUserInfo>(user);
                userInfo.Roles = GetUserRoles(user.Id);
                return userInfo;
            }
            return null;
        }
        /// <summary>
        /// 获取用户的角色集合
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        private List<UserRoles> GetUserRoles(int userId) {
            var userRoles = (from u in _userRepository.GetAll()
                             join ur in _userRoleRepository.GetAll()
                             on u.Id equals ur.UserId
                             join r in _roleRepository.GetAll()
                             on ur.RoleId equals r.Id
                             where u.Id == userId
                             select new UserRoles {
                                 Id = r.Id,
                                 Code = r.Code,
                                 Name = r.Name
                             }).ToList();
            return userRoles;
        }

        [UnitOfWork(IsDisabled = true)]
        public Page<GetUserOutput> GetUserList(GetUserInput input) {
            var result = _userRepository.GetAll().WhereIf(string.IsNullOrEmpty(input.Name), t => t.Name.Contains(input.Name))
                .Include(u => u.UserRoleList)
                .ThenInclude(ur => ur.Role);
            var pageResult = result.PageAndOrderBy(input);
            var output = new List<GetUserOutput>();
            foreach (var r in pageResult) {
                var userOutput = ObjectMapper.Map<GetUserOutput>(r);
                foreach (var ur in r.UserRoleList) {
                    userOutput.Roles.Add(new URole { Id = ur.RoleId, Name = ur.Role.Name });
                }
                output.Add(userOutput);
            }
            return new Page<GetUserOutput>(input, result.Count(), output);
        }

        public async Task AddOrUpdateUser(int? id, AddOrUpdateUserInput input) {
            if (id.HasValue) {
                var checkUserName = _userRepository.GetAll().Where(t => t.Id != id && t.UserName == input.UserName);
                if (checkUserName.Any()) {
                    throw new BusinessException($"用户名已存在");
                }
                var user = _userRepository.GetAll().Where(t => t.Id == id).FirstOrDefault();
                if (user == null)
                    throw new BusinessException($"找不到待更新的数据 ID：{id}");

                user.Name = input.Name;
                user.UserName = input.UserName;
                user.Status = input.Status;

                await _userRepository.UpdateAsync(user);

                _userRoleRepository.Delete(t => t.UserId == id);
                foreach (var roleId in input.RoleIds) {
                    var userRole = new UserRole {
                        UserId = (int)id,
                        RoleId = roleId
                    };
                    _userRoleRepository.Insert(userRole);
                }
            } else {
                var checkUserName = _userRepository.GetAll().Where(t => t.UserName == input.UserName);
                if (checkUserName.Any()) {
                    throw new BusinessException($"用户名已存在");
                }
                var user = new User {
                    Name = input.Name,
                    UserName = input.UserName,
                    Status = input.Status,
                    Password = MD5Helper.MD5Encrypt32("123456"),//初始密码
                    CreateTime = DateTime.Now,
                };
                var newId = await _userRepository.InsertAndGetIdAsync(user);
                if (newId > 0) {
                    foreach (var roleId in input.RoleIds) {
                        var userRole = new UserRole {
                            UserId = newId,
                            RoleId = roleId
                        };
                        _userRoleRepository.Insert(userRole);
                    }
                }
            }
        }

        public async Task DeleteUser(int[] ids) {
            //物理删除
            foreach (var id in ids) {
                await _userRepository.DeleteAsync(id);
            }

            await _userRoleRepository.DeleteAsync(t => ids.Contains(t.UserId));
        }

        public async Task ResetPassword(int id) {
            var user = _userRepository.GetAll().Where(t => t.Id == id).FirstOrDefault();
            if (user == null)
                throw new BusinessException($"找不到待更新的数据 ID：{id}");

            user.Password = MD5Helper.MD5Encrypt32("123456");

            await _userRepository.UpdateAsync(user);
        }

        public async Task ModifyPassword(ModifyPasswordInput input) {
            if (input.Password != input.Confirm) {
                throw new BusinessException($"两次输入的新密码不匹配");
            }
            var currentMd5Password = MD5Helper.MD5Encrypt32(input.Current);
            var user = await _userRepository.FirstOrDefaultAsync(t => t.Id == AdmSession.UserId && t.Password == currentMd5Password);
            if (user == null) {
                throw new BusinessException($"当前密码错误，请重试");
            }

            user.Password = MD5Helper.MD5Encrypt32(input.Password);
            await _userRepository.UpdateAsync(user);
        }
    }
}