package com.voxify.authorization.repository;

import com.voxify.authorization.entity.GlobalRole;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.stereotype.Repository;

import java.util.Optional;

@Repository
public interface GlobalRoleRepository extends JpaRepository<GlobalRole, String> {
    Optional<GlobalRole> findByUserId(String userId);
}
