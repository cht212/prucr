using CRM.Data.Services;

namespace CRM.Data.Tests;

public sealed class CrmPermissionServiceTests
{
    [Theory]
    [InlineData(CrmRoles.Administrador, true)]
    [InlineData(CrmRoles.Supervisor, true)]
    [InlineData(CrmRoles.Auditor, true)]
    [InlineData(CrmRoles.Asesor, false)]
    [InlineData(CrmRoles.Marketing, false)]
    public void AllChatsKeepsExistingRoleDefaults(string role, bool expected)
    {
        Assert.Equal(expected, CrmPermissionService.GetBasePermissions(role).Contains(CrmPermissionService.ViewAllChats));
    }

    [Fact]
    public void AllChatsCanBeGrantedAndRevokedWithoutGrantingActionsOrChannels()
    {
        var selected = CrmPermissionService.GetBasePermissions(CrmRoles.Asesor);
        selected.Add(CrmPermissionService.ViewAllChats);
        selected.Remove(CrmPermissionService.ViewInstagram);
        selected.Remove(CrmPermissionService.SendMessages);
        var effective = CrmPermissionService.ResolveEffective(CrmRoles.Asesor,
            CrmPermissionService.BuildOverrides(CrmRoles.Asesor, selected));
        Assert.Contains(CrmPermissionService.ViewAllChats, effective);
        Assert.DoesNotContain(CrmPermissionService.ViewInstagram, effective);
        Assert.DoesNotContain(CrmPermissionService.SendMessages, effective);
        Assert.DoesNotContain(CrmPermissionService.AssignConversations, effective);

        selected = CrmPermissionService.GetBasePermissions(CrmRoles.Supervisor);
        selected.Remove(CrmPermissionService.ViewAllChats);
        effective = CrmPermissionService.ResolveEffective(CrmRoles.Supervisor,
            CrmPermissionService.BuildOverrides(CrmRoles.Supervisor, selected));
        Assert.DoesNotContain(CrmPermissionService.ViewAllChats, effective);
    }

    [Theory]
    [InlineData(CrmRoles.Administrador)]
    [InlineData(CrmRoles.Supervisor)]
    [InlineData(CrmRoles.Asesor)]
    [InlineData(CrmRoles.Auditor)]
    [InlineData(CrmRoles.Marketing)]
    public void ExistingRolesKeepTheirModuleAccess(string role)
    {
        var effective = CrmPermissionService.ResolveEffective(role, []);
        foreach (var definition in CrmPermissionService.Catalog.Where(item => item.Code.StartsWith("modulo.")))
            Assert.Equal(CrmRolePermissions.CanAccessModule(role, definition.Code[7..]), effective.Contains(definition.Code));
    }

    [Fact]
    public void CustomSelectionCanRemoveInheritedMarketingAccessAndActions()
    {
        var stored = CrmPermissionService.BuildOverrides(CrmRoles.Marketing, []);
        Assert.Contains(CrmPermissionService.DeniedPrefix + CrmPermissionService.ModuleMarketing, stored);
        Assert.Empty(CrmPermissionService.ResolveEffective(CrmRoles.Marketing, stored));
    }

    [Fact]
    public void AuditorCanReceiveMarketingActionsWithoutChangingRole()
    {
        var selected = CrmPermissionService.GetBasePermissions(CrmRoles.Auditor);
        selected.Add(CrmPermissionService.ManageMarketing);
        selected.Remove(CrmPermissionService.ExportData);
        var stored = CrmPermissionService.BuildOverrides(CrmRoles.Auditor, selected);
        Assert.True(selected.SetEquals(CrmPermissionService.ResolveEffective(CrmRoles.Auditor, stored)));
    }

    [Fact]
    public void StoredAdditionalPermissionsRemainCompatibleAndDenialsWin()
    {
        var effective = CrmPermissionService.ResolveEffective(CrmRoles.Asesor,
            [CrmPermissionService.ModuleMarketing, CrmPermissionService.SendMessages,
             CrmPermissionService.DeniedPrefix + CrmPermissionService.SendMessages]);
        Assert.Contains(CrmPermissionService.ModuleMarketing, effective);
        Assert.DoesNotContain(CrmPermissionService.SendMessages, effective);
        Assert.DoesNotContain(effective, item => item.StartsWith(CrmPermissionService.DeniedPrefix));
    }

    [Fact]
    public void RestoringRoleSelectionRemovesOverrides()
    {
        Assert.Empty(CrmPermissionService.BuildOverrides(CrmRoles.Asesor,
            CrmPermissionService.GetBasePermissions(CrmRoles.Asesor)));
    }

    [Fact]
    public void UserOverridesAreCalculatedFromCustomRolePermissions()
    {
        var customRole = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmPermissionService.ModuleDashboard,
            CrmPermissionService.ModuleContacts,
            CrmPermissionService.CreateContacts
        };
        var userSelection = new HashSet<string>(customRole, StringComparer.OrdinalIgnoreCase)
        {
            CrmPermissionService.EditContacts
        };
        userSelection.Remove(CrmPermissionService.CreateContacts);

        var stored = CrmPermissionService.BuildOverrides(customRole, userSelection);
        var effective = CrmPermissionService.ApplyOverrides(customRole, stored);

        Assert.True(userSelection.SetEquals(effective));
        Assert.Contains(CrmPermissionService.DeniedPrefix + CrmPermissionService.CreateContacts, stored);
    }

    [Fact]
    public void AdministratorCannotLoseAccessThroughStoredOverrides()
    {
        var effective = CrmPermissionService.ResolveEffective(CrmRoles.Administrador,
            [CrmPermissionService.DeniedPrefix + CrmPermissionService.ModuleUsers]);
        Assert.Contains(CrmPermissionService.ModuleUsers, effective);
    }

    [Fact]
    public void InboxRolesReceiveCustomerDetailsAndEveryChannelByDefault()
    {
        var effective = CrmPermissionService.GetBasePermissions(CrmRoles.Asesor);
        Assert.Contains(CrmPermissionService.ViewCustomerDetails, effective);
        Assert.Equal(["WHATSAPP", "INSTAGRAM", "FACEBOOK", "TIKTOK"],
            CrmPermissionService.GetAllowedChannels(effective));
    }

    [Fact]
    public void CommunicationChannelsCanBeConfiguredIndividually()
    {
        var selected = CrmPermissionService.GetBasePermissions(CrmRoles.Asesor);
        selected.Remove(CrmPermissionService.ViewTikTok);
        selected.Remove(CrmPermissionService.ViewFacebook);
        selected.Remove(CrmPermissionService.ViewCustomerDetails);
        var effective = CrmPermissionService.ResolveEffective(CrmRoles.Asesor,
            CrmPermissionService.BuildOverrides(CrmRoles.Asesor, selected));
        Assert.Equal(["WHATSAPP", "INSTAGRAM"], CrmPermissionService.GetAllowedChannels(effective));
        Assert.DoesNotContain(CrmPermissionService.ViewCustomerDetails, effective);
    }

    [Fact]
    public void ContactOnlyCardCanBeGrantedWithoutFullCardOrContactsModule()
    {
        var selected = CrmPermissionService.GetBasePermissions(CrmRoles.Asesor);
        selected.Remove(CrmPermissionService.ViewCustomerDetails);
        selected.Remove(CrmPermissionService.ModuleContacts);
        selected.Remove(CrmPermissionService.EditContacts);
        selected.Add(CrmPermissionService.EditConversationContact);

        var effective = CrmPermissionService.ResolveEffective(CrmRoles.Asesor,
            CrmPermissionService.BuildOverrides(CrmRoles.Asesor, selected));

        Assert.Contains(CrmPermissionService.ModuleInbox, effective);
        Assert.Contains(CrmPermissionService.EditConversationContact, effective);
        Assert.DoesNotContain(CrmPermissionService.ViewCustomerDetails, effective);
        Assert.DoesNotContain(CrmPermissionService.EditContacts, effective);
        Assert.True(CrmPermissionService.IsContactUpdatePath("/api/crm/contactos/123"));
        Assert.False(CrmPermissionService.IsContactUpdatePath("/api/crm/contactos/123/conversaciones"));
    }

    [Fact]
    public void HidingModuleAlsoRemovesEveryPermissionInsideIt()
    {
        var selected = CrmPermissionService.GetBasePermissions(CrmRoles.Asesor);
        selected.Remove(CrmPermissionService.ModuleInbox);
        var effective = CrmPermissionService.ResolveEffective(CrmRoles.Asesor,
            CrmPermissionService.BuildOverrides(CrmRoles.Asesor, selected));
        Assert.DoesNotContain(CrmPermissionService.ModuleInbox, effective);
        Assert.DoesNotContain(CrmPermissionService.SendMessages, effective);
        Assert.Empty(CrmPermissionService.GetAllowedChannels(effective));
    }

    [Fact]
    public void LegacyModuleDenialAlsoInvalidatesItsInheritedChildren()
    {
        var effective = CrmPermissionService.ResolveEffective(CrmRoles.Asesor,
            [CrmPermissionService.DeniedPrefix + CrmPermissionService.ModuleInbox]);
        Assert.DoesNotContain(CrmPermissionService.SendMessages, effective);
        Assert.DoesNotContain(CrmPermissionService.ViewCustomerDetails, effective);
        Assert.Empty(CrmPermissionService.GetAllowedChannels(effective));
    }

    [Theory]
    [InlineData("/api/crm/leads", CrmPermissionService.ModuleLeads)]
    [InlineData("/api/campanas", CrmPermissionService.ModuleMarketing)]
    [InlineData("/api/crm/comentarios", CrmPermissionService.ModuleMarketing)]
    [InlineData("/api/crm/contactos/exportar", CrmPermissionService.ExportData)]
    [InlineData("/api/crm/reportes/exportar", CrmPermissionService.ExportData)]
    public void ReadEndpointsRequireTheirPermission(string path, string permission)
    {
        Assert.Contains(permission, CrmPermissionService.ResolveReadPermissions(path));
    }
}
