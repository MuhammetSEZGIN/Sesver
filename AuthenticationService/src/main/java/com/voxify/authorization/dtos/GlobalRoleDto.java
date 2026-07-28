package com.voxify.authorization.dtos;

import lombok.AllArgsConstructor;
import lombok.Builder;
import lombok.Data;
import lombok.NoArgsConstructor;

import java.io.Serializable;

@Data
@Builder
@AllArgsConstructor
@NoArgsConstructor
public class GlobalRoleDto implements Serializable {
    private String globalRoleId;
    private String userId;

    /**
     * Kullanicinin global rolu; rol tanimli degilse null doner.
     */
    private String roles;
}
