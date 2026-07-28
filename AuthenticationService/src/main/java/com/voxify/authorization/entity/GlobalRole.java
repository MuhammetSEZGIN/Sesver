package com.voxify.authorization.entity;

import jakarta.persistence.*;
import lombok.AllArgsConstructor;
import lombok.Builder;
import lombok.Data;
import lombok.NoArgsConstructor;

/**
 * Klandan bagimsiz sistem rolu. Bir kullanicinin en fazla bir global rolu olur.
 */
@Entity
@Table(name = "global_roles")
@Data
@Builder
@AllArgsConstructor
@NoArgsConstructor
public class GlobalRole {
    @Id
    @GeneratedValue(strategy = GenerationType.UUID)
    private String globalRoleId;

    @Column(nullable = false, unique = true)
    private String userId;

    @Column(nullable = false)
    private String roles;
}
