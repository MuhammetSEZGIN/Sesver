package com.voxify.authorization.controller;

import com.voxify.authorization.dtos.GlobalRoleDto;
import com.voxify.authorization.service.GlobalRoleService;
import jakarta.validation.constraints.NotBlank;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.validation.annotation.Validated;
import org.springframework.web.bind.annotation.*;

/**
 * Klandan bagimsiz sistem rolleri. Klan ici roller {@link RoleController} altinda.
 *
 * Sadece okuma yapar; klan rollerinde oldugu gibi burada da yazma islemleri
 * HTTP'ye acilmaz. Global rol atamasi elle SQL ile yapilir.
 */
@RestController
@RequiredArgsConstructor
@RequestMapping("global-roles")
@Validated
public class GlobalRoleController {
    private final GlobalRoleService globalRoleService;

    @GetMapping
    public ResponseEntity<GlobalRoleDto> getGlobalRole(
            @RequestParam @NotBlank(message = "userId bos olamaz") String userId) {
        return ResponseEntity.ok(globalRoleService.getGlobalRole(userId));
    }
}
