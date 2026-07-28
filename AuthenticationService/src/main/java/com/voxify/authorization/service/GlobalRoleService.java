package com.voxify.authorization.service;

import com.voxify.authorization.dtos.GlobalRoleDto;
import com.voxify.authorization.entity.GlobalRole;
import com.voxify.authorization.repository.GlobalRoleRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.cache.annotation.Cacheable;
import org.springframework.stereotype.Service;

import java.util.Optional;

/**
 * Global rolleri okur. Rol atama/kaldirma HTTP'ye acilmaz; kayitlar
 * global_roles tablosuna elle SQL ile yazilir.
 */
@Service
@Slf4j
@RequiredArgsConstructor
public class GlobalRoleService {
    private final GlobalRoleRepository globalRoleRepository;

    /**
     * Kullanicinin global rolunu doner. Rol tanimli degilse roles alani null olur;
     * bu sorgu her admin isteginde calistigi icin "rol yok" durumu hata degildir.
     */
    @Cacheable(value = "globalRoles", key = "'u:' + #userId")
    public GlobalRoleDto getGlobalRole(String userId) {
        Optional<GlobalRole> existingRole = globalRoleRepository.findByUserId(userId);

        if (existingRole.isEmpty()) {
            log.debug("Global rol bulunamadi: userId={}", userId);
            return GlobalRoleDto.builder().userId(userId).roles(null).build();
        }

        GlobalRole role = existingRole.get();
        log.info("Global rol getirildi: userId={}, role={}", userId, role.getRoles());
        return mapToDto(role);
    }

    private GlobalRoleDto mapToDto(GlobalRole role) {
        return GlobalRoleDto.builder()
                .globalRoleId(role.getGlobalRoleId())
                .userId(role.getUserId())
                .roles(role.getRoles())
                .build();
    }
}
