using System.Globalization;
using System.Text;
using System.Xml.Linq;
using TwinCatAutomationKit.Abstractions;

namespace TwinCatAutomationKit.TwinCat;

public sealed class TwinCatTsprojMutationService
{
    public void ApplyMutationPlan(string tsprojPath, ApplyTsprojMutationPlanRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        XDocument document = Load(tsprojPath);
        ApplyMutationPlanInternal(document, request);
        Save(document, tsprojPath);
    }

    public void UpsertElement(string tsprojPath, TsprojElementUpsertRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        XDocument document = Load(tsprojPath);
        ApplyElementUpsert(document, request);
        Save(document, tsprojPath);
    }

    public void UpsertFragment(string tsprojPath, TsprojFragmentUpsertRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        XDocument document = Load(tsprojPath);
        ApplyFragmentUpsert(document, request);
        Save(document, tsprojPath);
    }

    public void EnsureTaskDefinition(string tsprojPath, EnsureTaskDefinitionRequest request)
    {
        ValidateRequiredText(request.TaskName, nameof(request.TaskName));

        XDocument document = Load(tsprojPath);
        XElement container = FindOrCreateCanonicalTasksContainer(document);
        XElement? task = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Task" &&
            string.Equals(GetChildElementValue(element, "Name"), request.TaskName, StringComparison.OrdinalIgnoreCase));

        if (task is null)
        {
            task = new XElement(container.GetDefaultNamespace() + "Task", new XElement(container.GetDefaultNamespace() + "Name", request.TaskName));
            container.Add(task);
        }
        else if (!ReferenceEquals(task.Parent, container))
        {
            // Normalize location so offline-created tasks can be discovered by TwinCAT UI after reopen.
            task.Remove();
            container.Add(task);
        }

        task.SetAttributeValue("Priority", request.Priority.ToString(CultureInfo.InvariantCulture));
        task.SetAttributeValue("CycleTime", (request.CycleTimeNs / 100).ToString(CultureInfo.InvariantCulture));
        task.SetAttributeValue("AmsPort", request.AmsPort.ToString(CultureInfo.InvariantCulture));
        if (request.IoAtBegin.HasValue)
        {
            task.SetAttributeValue("IoAtBegin", request.IoAtBegin.Value ? "true" : "false");
        }

        Save(document, tsprojPath);
    }

    public void ClearTaskLayout(string tsprojPath, ClearTaskLayoutRequest request)
    {
        ValidateRequiredText(request.TaskName, nameof(request.TaskName));

        XDocument document = Load(tsprojPath);
        XElement task = FindTaskByName(document, request.TaskName);

        IEnumerable<XElement> targets = task.Elements().Where(element =>
            (request.RemoveVars && element.Name.LocalName == "Vars") ||
            (request.RemoveImage && element.Name.LocalName == "Image"));
        foreach (XElement target in targets.ToList())
        {
            target.Remove();
        }

        Save(document, tsprojPath);
    }

    public void EnsureTaskVarsGroup(string tsprojPath, EnsureTaskVarsGroupRequest request)
    {
        ValidateRequiredText(request.TaskName, nameof(request.TaskName));
        ValidateRequiredText(request.GroupName, nameof(request.GroupName));
        ValidateRequiredText(request.BaseVarName, nameof(request.BaseVarName));
        ValidateRequiredText(request.TypeName, nameof(request.TypeName));

        if (request.Count <= 0)
        {
            throw new InvalidOperationException("Task Vars Count must be greater than zero.");
        }

        if (request.StartIndex <= 0)
        {
            throw new InvalidOperationException("Task Vars StartIndex must be greater than zero.");
        }

        if (request.BitStride <= 0)
        {
            throw new InvalidOperationException("Task Vars BitStride must be greater than zero.");
        }

        if (request.ExternalAddressStride < 0)
        {
            throw new InvalidOperationException("Task Vars ExternalAddressStride must be greater than or equal to zero.");
        }

        XDocument document = Load(tsprojPath);
        XElement task = FindTaskByName(document, request.TaskName);
        List<XElement> existingGroups = task.Elements().Where(element =>
                element.Name.LocalName == "Vars" &&
                string.Equals(GetChildElementValue(element, "Name"), request.GroupName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (existingGroups.Count > 0)
        {
            if (!request.ReplaceExistingGroup)
            {
                Save(document, tsprojPath);
                return;
            }

            foreach (XElement existingGroup in existingGroups)
            {
                existingGroup.Remove();
            }
        }

        XElement varsGroup = CreateTaskVars(
            defaultNamespace: task.GetDefaultNamespace(),
            varGrpType: request.VarGrpType,
            insertType: request.InsertType,
            groupName: request.GroupName,
            baseVarName: request.BaseVarName,
            typeName: request.TypeName,
            count: request.Count,
            bitStride: request.BitStride,
            externalAddressStride: request.ExternalAddressStride,
            firstExternalAddress: request.FirstExternalAddress,
            startIndex: request.StartIndex);

        XElement? firstImage = task.Elements().FirstOrDefault(element => element.Name.LocalName == "Image");
        if (firstImage is not null)
        {
            firstImage.AddBeforeSelf(varsGroup);
        }
        else
        {
            task.Add(varsGroup);
        }

        Save(document, tsprojPath);
    }

    public void EnsureTaskImage(string tsprojPath, EnsureTaskImageRequest request)
    {
        ValidateRequiredText(request.TaskName, nameof(request.TaskName));
        ValidateRequiredText(request.ImageName, nameof(request.ImageName));

        if (request.ImageId <= 0)
        {
            throw new InvalidOperationException("Task Image Id must be greater than zero.");
        }

        if (request.SizeIn < 0 || request.SizeOut < 0)
        {
            throw new InvalidOperationException("Task Image SizeIn and SizeOut must be greater than or equal to zero.");
        }

        XDocument document = Load(tsprojPath);
        XElement task = FindTaskByName(document, request.TaskName);
        if (request.IoAtBegin.HasValue)
        {
            task.SetAttributeValue("IoAtBegin", request.IoAtBegin.Value ? "true" : "false");
        }

        if (request.ReplaceExistingImage)
        {
            foreach (XElement existingImage in task.Elements().Where(element => element.Name.LocalName == "Image").ToList())
            {
                existingImage.Remove();
            }
        }

        string imageId = request.ImageId.ToString(CultureInfo.InvariantCulture);
        XElement image = task.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Image" &&
                string.Equals(GetAttributeValue(element, "Id"), imageId, StringComparison.OrdinalIgnoreCase))
            ?? task.Elements().FirstOrDefault(element => element.Name.LocalName == "Image")
            ?? AddChild(task, "Image");

        image.SetAttributeValue("Id", imageId);
        image.SetAttributeValue("AddrType", request.AddressType.ToString(CultureInfo.InvariantCulture));
        image.SetAttributeValue("ImageType", request.ImageType.ToString(CultureInfo.InvariantCulture));
        image.SetAttributeValue("SizeIn", request.SizeIn.ToString(CultureInfo.InvariantCulture));
        image.SetAttributeValue("SizeOut", request.SizeOut.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(image, "Name", request.ImageName);

        Save(document, tsprojPath);
    }

    public void BindInstanceContext(string tsprojPath, BindInstanceContextRequest request)
    {
        ValidateRequiredText(request.InstanceName, nameof(request.InstanceName));
        ValidateObjectIdText(request.TaskObjectId, nameof(request.TaskObjectId));

        XDocument document = Load(tsprojPath);
        XElement instance = FindInstance(document, request.InstanceName);
        XElement tmcDesc = GetOrCreateTmcDesc(instance);
        XElement contexts = GetOrCreateChildElement(tmcDesc, "Contexts");
        string contextId = request.ContextId.ToString(CultureInfo.InvariantCulture);
        XElement context = contexts.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Context" &&
                string.Equals(GetChildElementValue(element, "Id"), contextId, StringComparison.OrdinalIgnoreCase))
            ?? contexts.Elements().FirstOrDefault(element => element.Name.LocalName == "Context")
            ?? AddChild(contexts, "Context");
        XElement manualConfig = GetOrCreateChildElement(context, "ManualConfig");

        SetOrCreateChildElementValue(context, "Id", contextId);
        if (!string.IsNullOrWhiteSpace(request.ContextName))
        {
            SetOrCreateChildElementValue(context, "Name", request.ContextName);
        }

        SetOrCreateChildElementValue(manualConfig, "OTCID", request.TaskObjectId);
        SetOrCreateChildElementValue(context, "Priority", request.Priority.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(context, "CycleTime", request.CycleTimeNs.ToString(CultureInfo.InvariantCulture));

        XElement? pointers = tmcDesc.Elements().FirstOrDefault(element => element.Name.LocalName == "InterfacePointerValues");
        if (request.IncludeCyclicCaller)
        {
            pointers ??= AddChild(tmcDesc, "InterfacePointerValues");
            XElement value = FindNamedValue(pointers, "CyclicCaller") ?? CreateNamedValue(pointers, "CyclicCaller");
            SetOrCreateChildElementValue(value, "OTCID", request.TaskObjectId);
        }
        else if (request.RemoveCyclicCallerWhenExcluded && pointers is not null)
        {
            foreach (XElement cyclicCaller in pointers.Elements().Where(element =>
                         element.Name.LocalName == "Value" &&
                         string.Equals(GetChildElementValue(element, "Name"), "CyclicCaller", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                cyclicCaller.Remove();
            }

            if (!pointers.Elements().Any())
            {
                pointers.Remove();
            }
        }

        Save(document, tsprojPath);
    }

    public void BindInstanceToTask(string tsprojPath, BindInstanceToTaskRequest request)
    {
        BindInstanceContext(
            tsprojPath,
            new BindInstanceContextRequest(
                request.InstanceName,
                request.TaskObjectId,
                request.Priority,
                request.CycleTimeNs,
                ContextId: 1,
                ContextName: null,
                request.IncludeCyclicCaller,
                RemoveCyclicCallerWhenExcluded: false));
    }

    public void EnsureCppInstance(string tsprojPath, EnsureCppInstanceRequest request)
    {
        ValidateRequiredText(request.CppProjectName, nameof(request.CppProjectName));
        ValidateRequiredText(request.InstanceName, nameof(request.InstanceName));
        ValidateObjectIdText(request.ObjectId, nameof(request.ObjectId));

        XDocument document = Load(tsprojPath);
        XElement cppProject = FindCppProject(document, request.CppProjectName);
        XElement instance = cppProject.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Instance" &&
                string.Equals(GetChildElementValue(element, "Name"), request.InstanceName, StringComparison.OrdinalIgnoreCase))
            ?? AddChild(cppProject, "Instance");

        instance.SetAttributeValue("OTCID", request.ObjectId);
        SetOrCreateChildElementValue(instance, "Name", request.InstanceName);

        XElement tmcDesc = GetOrCreateTmcDesc(instance);
        XElement contexts = GetOrCreateChildElement(tmcDesc, "Contexts");
        string contextId = request.ContextId.ToString(CultureInfo.InvariantCulture);
        XElement context = contexts.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Context" &&
                string.Equals(GetChildElementValue(element, "Id"), contextId, StringComparison.OrdinalIgnoreCase))
            ?? contexts.Elements().FirstOrDefault(element => element.Name.LocalName == "Context")
            ?? AddChild(contexts, "Context");

        SetOrCreateChildElementValue(context, "Id", contextId);
        if (!string.IsNullOrWhiteSpace(request.ContextName))
        {
            SetOrCreateChildElementValue(context, "Name", request.ContextName);
        }

        XElement manualConfig = GetOrCreateChildElement(context, "ManualConfig");
        _ = GetOrCreateChildElement(manualConfig, "OTCID");
        foreach (XElement stale in manualConfig.Elements()
                     .Where(element => element.Name.LocalName is "Priority" or "CycleTime")
                     .ToList())
        {
            stale.Remove();
        }

        SetOrCreateChildElementValue(context, "Priority", request.Priority.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(context, "CycleTime", request.CycleTimeNs.ToString(CultureInfo.InvariantCulture));

        _ = GetOrCreateChildElement(tmcDesc, "ParameterValues");
        _ = GetOrCreateChildElement(tmcDesc, "InterfacePointerValues");
        _ = GetOrCreateChildElement(tmcDesc, "DataPointerValues");

        Save(document, tsprojPath);
    }

    public void EnsurePlcInstance(string tsprojPath, EnsurePlcInstanceRequest request)
    {
        ValidateRequiredText(request.PlcProjectName, nameof(request.PlcProjectName));
        ValidateRequiredText(request.PlcInstanceName, nameof(request.PlcInstanceName));

        XDocument document = Load(tsprojPath);
        XElement plcProject = FindOrCreatePlcProject(document, request.PlcProjectName);
        XElement instance = plcProject.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Instance" &&
                string.Equals(GetChildElementValue(element, "Name"), request.PlcInstanceName, StringComparison.OrdinalIgnoreCase))
            ?? AddChild(plcProject, "Instance");

        SetOrCreateChildElementValue(instance, "Name", request.PlcInstanceName);
        Save(document, tsprojPath);
    }

    public void BindPlcInstanceToTask(string tsprojPath, BindPlcInstanceToTaskRequest request)
    {
        ValidateRequiredText(request.PlcProjectName, nameof(request.PlcProjectName));
        ValidateRequiredText(request.PlcInstanceName, nameof(request.PlcInstanceName));
        ValidateRequiredText(request.PlcTaskName, nameof(request.PlcTaskName));
        ValidateObjectIdText(request.TaskObjectId, nameof(request.TaskObjectId));

        XDocument document = Load(tsprojPath);
        XElement plcInstance = FindPlcInstance(document, request.PlcProjectName, request.PlcInstanceName);
        XElement contexts = GetOrCreateChildElement(plcInstance, "Contexts");
        XElement context = ResolveOrCreatePlcContext(contexts, request.PlcTaskName, request.ContextId);
        XElement manualConfig = GetOrCreateChildElement(context, "ManualConfig");

        SetOrCreateChildElementValue(context, "Id", request.ContextId.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(context, "Name", request.PlcTaskName);
        SetOrCreateChildElementValue(manualConfig, "OTCID", request.TaskObjectId);
        SetOrCreateChildElementValue(context, "Priority", request.Priority.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(context, "CycleTime", request.CycleTimeNs.ToString(CultureInfo.InvariantCulture));

        Save(document, tsprojPath);
    }

    public void SetTaskAffinity(string tsprojPath, SetTaskAffinityRequest request)
    {
        ValidateRequiredText(request.TaskName, nameof(request.TaskName));
        ValidateRequiredText(request.Affinity, nameof(request.Affinity));

        XDocument document = Load(tsprojPath);
        XElement task = FindTaskByName(document, request.TaskName);
        task.SetAttributeValue("Affinity", request.Affinity);
        task.SetAttributeValue("AdtTasks", request.EnableAdtTasks ? "true" : "false");
        Save(document, tsprojPath);
    }

    public void SetPlcProjectProperties(string tsprojPath, SetPlcProjectPropertiesRequest request)
    {
        ValidateRequiredText(request.PlcProjectName, nameof(request.PlcProjectName));
        if (request.AmsPort.HasValue && request.AmsPort.Value < 0)
        {
            throw new InvalidOperationException("PLC project AmsPort must be greater than or equal to zero.");
        }

        XDocument document = Load(tsprojPath);
        XElement plcProject = FindPlcProject(document, request.PlcProjectName);

        if (request.ProjectFilePath is not null)
        {
            plcProject.SetAttributeValue("PrjFilePath", string.IsNullOrWhiteSpace(request.ProjectFilePath) ? null : request.ProjectFilePath);
        }

        if (request.TmcFilePath is not null)
        {
            plcProject.SetAttributeValue("TmcFilePath", string.IsNullOrWhiteSpace(request.TmcFilePath) ? null : request.TmcFilePath);
        }

        if (request.ReloadTmc.HasValue)
        {
            plcProject.SetAttributeValue("ReloadTmc", request.ReloadTmc.Value ? "true" : "false");
        }

        if (request.AmsPort.HasValue)
        {
            plcProject.SetAttributeValue("AmsPort", request.AmsPort.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (request.FileArchiveSettings is not null)
        {
            plcProject.SetAttributeValue("FileArchiveSettings", string.IsNullOrWhiteSpace(request.FileArchiveSettings) ? null : request.FileArchiveSettings);
        }

        Save(document, tsprojPath);
    }

    public void SetPlcInstanceMetadata(string tsprojPath, SetPlcInstanceMetadataRequest request)
    {
        ValidateRequiredText(request.PlcProjectName, nameof(request.PlcProjectName));
        ValidateRequiredText(request.PlcInstanceName, nameof(request.PlcInstanceName));

        XDocument document = Load(tsprojPath);
        XElement plcInstance = FindPlcInstance(document, request.PlcProjectName, request.PlcInstanceName);

        if (request.TcSmClass is not null)
        {
            plcInstance.SetAttributeValue("TcSmClass", string.IsNullOrWhiteSpace(request.TcSmClass) ? null : request.TcSmClass);
        }

        if (request.TmcPath is not null)
        {
            plcInstance.SetAttributeValue("TmcPath", string.IsNullOrWhiteSpace(request.TmcPath) ? null : request.TmcPath);
        }

        if (request.KeepUnrestoredLinks is not null)
        {
            plcInstance.SetAttributeValue("KeepUnrestoredLinks", string.IsNullOrWhiteSpace(request.KeepUnrestoredLinks) ? null : request.KeepUnrestoredLinks);
        }

        if (request.Clsid is not null || request.ClassFactory is not null)
        {
            XElement clsid = GetOrCreateChildElement(plcInstance, "CLSID");
            if (request.ClassFactory is not null)
            {
                clsid.SetAttributeValue("ClassFactory", string.IsNullOrWhiteSpace(request.ClassFactory) ? null : request.ClassFactory);
            }

            if (request.Clsid is not null)
            {
                clsid.Value = request.Clsid;
            }
        }

        Save(document, tsprojPath);
    }

    public void ClearPlcInstanceVars(string tsprojPath, ClearPlcInstanceVarsRequest request)
    {
        ValidateRequiredText(request.PlcProjectName, nameof(request.PlcProjectName));
        ValidateRequiredText(request.PlcInstanceName, nameof(request.PlcInstanceName));

        XDocument document = Load(tsprojPath);
        XElement plcInstance = FindPlcInstance(document, request.PlcProjectName, request.PlcInstanceName);
        foreach (XElement vars in plcInstance.Elements().Where(element => element.Name.LocalName == "Vars").ToList())
        {
            vars.Remove();
        }

        Save(document, tsprojPath);
    }

    public void EnsurePlcInstanceVarsGroup(string tsprojPath, EnsurePlcInstanceVarsGroupRequest request)
    {
        ValidateRequiredText(request.PlcProjectName, nameof(request.PlcProjectName));
        ValidateRequiredText(request.PlcInstanceName, nameof(request.PlcInstanceName));
        ValidateRequiredText(request.GroupName, nameof(request.GroupName));
        foreach (PlcInstanceVarItem item in request.Variables ?? Array.Empty<PlcInstanceVarItem>())
        {
            ValidateRequiredText(item.Name, nameof(item.Name));
            ValidateRequiredText(item.Type, nameof(item.Type));
            if (item.BitOffset.HasValue && item.BitOffset.Value < 0)
            {
                throw new InvalidOperationException("PLC variable BitOffset must be greater than or equal to zero.");
            }

            if (item.ExternalAddress.HasValue && item.ExternalAddress.Value < 0)
            {
                throw new InvalidOperationException("PLC variable ExternalAddress must be greater than or equal to zero.");
            }
        }

        XDocument document = Load(tsprojPath);
        XElement plcInstance = FindPlcInstance(document, request.PlcProjectName, request.PlcInstanceName);
        List<XElement> existingGroups = plcInstance.Elements().Where(element =>
                element.Name.LocalName == "Vars" &&
                string.Equals(GetChildElementValue(element, "Name"), request.GroupName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (existingGroups.Count > 0)
        {
            if (!request.ReplaceExistingGroup)
            {
                Save(document, tsprojPath);
                return;
            }

            foreach (XElement existing in existingGroups)
            {
                existing.Remove();
            }
        }

        XElement varsGroup = new(plcInstance.GetDefaultNamespace() + "Vars");
        varsGroup.SetAttributeValue("VarGrpType", request.VarGrpType.ToString(CultureInfo.InvariantCulture));
        varsGroup.SetAttributeValue("InsertType", request.InsertType.ToString(CultureInfo.InvariantCulture));
        if (request.AreaNo.HasValue)
        {
            varsGroup.SetAttributeValue("AreaNo", request.AreaNo.Value.ToString(CultureInfo.InvariantCulture));
        }

        SetOrCreateChildElementValue(varsGroup, "Name", request.GroupName);
        foreach (PlcInstanceVarItem item in request.Variables ?? Array.Empty<PlcInstanceVarItem>())
        {
            XElement varElement = AddChild(varsGroup, "Var");
            SetOrCreateChildElementValue(varElement, "Name", item.Name);
            SetOrCreateChildElementValue(varElement, "Type", item.Type);
            if (item.BitOffset.HasValue)
            {
                SetOrCreateChildElementValue(varElement, "BitOffs", item.BitOffset.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (item.ExternalAddress.HasValue)
            {
                SetOrCreateChildElementValue(varElement, "ExternalAddress", item.ExternalAddress.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        XElement? contexts = plcInstance.Elements().FirstOrDefault(element => element.Name.LocalName == "Contexts");
        if (contexts is not null)
        {
            contexts.AddBeforeSelf(varsGroup);
        }
        else
        {
            plcInstance.Add(varsGroup);
        }

        Save(document, tsprojPath);
    }

    public void ClearPlcInitSymbols(string tsprojPath, ClearPlcInitSymbolsRequest request)
    {
        ValidateRequiredText(request.PlcProjectName, nameof(request.PlcProjectName));
        ValidateRequiredText(request.PlcInstanceName, nameof(request.PlcInstanceName));

        XDocument document = Load(tsprojPath);
        XElement plcInstance = FindPlcInstance(document, request.PlcProjectName, request.PlcInstanceName);
        XElement? initSymbols = plcInstance.Elements().FirstOrDefault(element => element.Name.LocalName == "InitSymbols");
        if (initSymbols is null)
        {
            Save(document, tsprojPath);
            return;
        }

        foreach (XElement symbol in initSymbols.Elements().Where(element => element.Name.LocalName == "InitSymbol").ToList())
        {
            symbol.Remove();
        }

        if (request.RemoveContainerWhenEmpty && !initSymbols.Elements().Any())
        {
            initSymbols.Remove();
        }

        Save(document, tsprojPath);
    }

    public void ClearPlcTaskPouOids(string tsprojPath, ClearPlcTaskPouOidsRequest request)
    {
        ValidateRequiredText(request.PlcProjectName, nameof(request.PlcProjectName));
        ValidateRequiredText(request.PlcInstanceName, nameof(request.PlcInstanceName));

        XDocument document = Load(tsprojPath);
        XElement plcInstance = FindPlcInstance(document, request.PlcProjectName, request.PlcInstanceName);
        XElement? taskPouOids = plcInstance.Elements().FirstOrDefault(element => element.Name.LocalName == "TaskPouOids");
        if (taskPouOids is null)
        {
            Save(document, tsprojPath);
            return;
        }

        foreach (XElement entry in taskPouOids.Elements().Where(element => element.Name.LocalName == "TaskPouOid").ToList())
        {
            entry.Remove();
        }

        if (request.RemoveContainerWhenEmpty && !taskPouOids.Elements().Any())
        {
            taskPouOids.Remove();
        }

        Save(document, tsprojPath);
    }

    public void ClearMappings(string tsprojPath, ClearMappingsRequest request)
    {
        XDocument document = Load(tsprojPath);
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");

        foreach (XElement mappings in root.Elements().Where(element => element.Name.LocalName == "Mappings").ToList())
        {
            mappings.Remove();
        }

        Save(document, tsprojPath);
    }

    public void ClearUnrestoredVarLinks(string tsprojPath, ClearUnrestoredVarLinksRequest request)
    {
        XDocument document = Load(tsprojPath);
        foreach (XElement links in document.Descendants().Where(element => element.Name.LocalName == "UnrestoredVarLinks").ToList())
        {
            links.Remove();
        }

        Save(document, tsprojPath);
    }

    public void ReplaceMappingsSection(string tsprojPath, ReplaceMappingsSectionRequest request)
    {
        XDocument document = Load(tsprojPath);
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");

        foreach (XElement mappings in root.Elements().Where(element => element.Name.LocalName == "Mappings").ToList())
        {
            mappings.Remove();
        }

        XElement mappingsFragment = ParseAndNormalizeSectionFragment(request.MappingsXml, root.GetDefaultNamespace(), "Mappings");
        root.Add(mappingsFragment);
        Save(document, tsprojPath);
    }

    public void ReplaceProjectIoSection(string tsprojPath, ReplaceProjectIoSectionRequest request)
    {
        XDocument document = Load(tsprojPath);
        XElement project = FindTopLevelProject(document);

        foreach (XElement ioSection in project.Elements().Where(element => element.Name.LocalName == "Io").ToList())
        {
            ioSection.Remove();
        }

        XElement ioFragment = ParseAndNormalizeSectionFragment(request.IoXml, project.GetDefaultNamespace(), "Io");
        project.Add(ioFragment);
        Save(document, tsprojPath);
    }

    public void ReplaceDataTypesSection(string tsprojPath, ReplaceDataTypesSectionRequest request)
    {
        XDocument document = Load(tsprojPath);
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");

        foreach (XElement dataTypes in root.Elements().Where(element => element.Name.LocalName == "DataTypes").ToList())
        {
            dataTypes.Remove();
        }

        XElement dataTypesFragment = ParseAndNormalizeSectionFragment(request.DataTypesXml, root.GetDefaultNamespace(), "DataTypes");
        XElement? project = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Project");
        if (request.InsertBeforeProject && project is not null)
        {
            project.AddBeforeSelf(dataTypesFragment);
        }
        else
        {
            root.Add(dataTypesFragment);
        }

        Save(document, tsprojPath);
    }

    public void ReplaceSystemSettingsSection(string tsprojPath, ReplaceSystemSettingsSectionRequest request)
    {
        XDocument document = Load(tsprojPath);
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        XElement system = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "System")
            ?? AddChild(root, "System");

        foreach (XElement settings in system.Elements().Where(element => element.Name.LocalName == "Settings").ToList())
        {
            settings.Remove();
        }

        XElement settingsFragment = ParseAndNormalizeSectionFragment(request.SettingsXml, system.GetDefaultNamespace(), "Settings");
        XElement? tasks = system.Elements().FirstOrDefault(element => element.Name.LocalName == "Tasks");
        if (request.InsertBeforeTasks && tasks is not null)
        {
            tasks.AddBeforeSelf(settingsFragment);
        }
        else
        {
            system.Add(settingsFragment);
        }

        Save(document, tsprojPath);
    }

    public void ApplyInstanceParameterPlan(string tsprojPath, ApplyInstanceParameterPlanRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        foreach (InstanceParameterMutation item in request.Items ?? Array.Empty<InstanceParameterMutation>())
        {
            ValidateRequiredText(item.InstanceName, nameof(item.InstanceName));
            ValidateRequiredText(item.ParameterName, nameof(item.ParameterName));
        }

        XDocument document = Load(tsprojPath);
        foreach (InstanceParameterMutation item in request.Items ?? Array.Empty<InstanceParameterMutation>())
        {
            EnsureParameterValueInDocument(
                document,
                new EnsureParameterValueRequest(
                    item.InstanceName,
                    item.ParameterName,
                    item.ValueText,
                    item.EnumText,
                    item.StringText));
        }

        Save(document, tsprojPath);
    }

    public void ClearInstanceParameterValues(string tsprojPath, ClearInstanceParameterValuesRequest request)
    {
        ValidateRequiredText(request.InstanceName, nameof(request.InstanceName));

        XDocument document = Load(tsprojPath);
        XElement instance = FindInstance(document, request.InstanceName);
        XElement tmcDesc = GetOrCreateTmcDesc(instance);
        XElement? parameterValues = tmcDesc.Elements().FirstOrDefault(element => element.Name.LocalName == "ParameterValues");
        if (parameterValues is null)
        {
            if (!request.RemoveContainerWhenEmpty)
            {
                _ = AddChild(tmcDesc, "ParameterValues");
            }

            Save(document, tsprojPath);
            return;
        }

        parameterValues.Elements().Remove();
        if (request.RemoveContainerWhenEmpty)
        {
            parameterValues.Remove();
        }

        Save(document, tsprojPath);
    }

    public void ClearInstanceDataPointerValues(string tsprojPath, ClearInstanceDataPointerValuesRequest request)
    {
        ValidateRequiredText(request.InstanceName, nameof(request.InstanceName));

        XDocument document = Load(tsprojPath);
        XElement instance = FindInstance(document, request.InstanceName);
        XElement tmcDesc = GetOrCreateTmcDesc(instance);
        XElement? dataPointerValues = tmcDesc.Elements().FirstOrDefault(element => element.Name.LocalName == "DataPointerValues");
        if (dataPointerValues is null)
        {
            if (!request.RemoveContainerWhenEmpty)
            {
                _ = AddChild(tmcDesc, "DataPointerValues");
            }

            Save(document, tsprojPath);
            return;
        }

        dataPointerValues.Elements().Remove();
        if (request.RemoveContainerWhenEmpty)
        {
            dataPointerValues.Remove();
        }

        Save(document, tsprojPath);
    }

    public void ApplyInstanceInterfacePointerPlan(string tsprojPath, ApplyInstanceInterfacePointerPlanRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        foreach (InstanceInterfacePointerMutation item in request.Items ?? Array.Empty<InstanceInterfacePointerMutation>())
        {
            ValidateRequiredText(item.InstanceName, nameof(item.InstanceName));
            ValidateRequiredText(item.PointerName, nameof(item.PointerName));
            ValidateObjectIdText(item.ObjectId, nameof(item.ObjectId));
        }

        XDocument document = Load(tsprojPath);
        foreach (InstanceInterfacePointerMutation item in request.Items ?? Array.Empty<InstanceInterfacePointerMutation>())
        {
            EnsureInterfacePointerValueInDocument(
                document,
                new EnsureInterfacePointerValueRequest(
                    item.InstanceName,
                    item.PointerName,
                    item.ObjectId));
        }

        Save(document, tsprojPath);
    }

    public void ApplyInstanceDataPointerPlan(string tsprojPath, ApplyInstanceDataPointerPlanRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        foreach (InstanceDataPointerMutation item in request.Items ?? Array.Empty<InstanceDataPointerMutation>())
        {
            ValidateDataPointerRequest(new EnsureDataPointerValueRequest(
                item.InstanceName,
                item.PointerName,
                item.ObjectId,
                item.AreaNo,
                item.ByteOffset,
                item.ByteSize));
        }

        XDocument document = Load(tsprojPath);
        foreach (InstanceDataPointerMutation item in request.Items ?? Array.Empty<InstanceDataPointerMutation>())
        {
            EnsureDataPointerValueInDocument(
                document,
                new EnsureDataPointerValueRequest(
                    item.InstanceName,
                    item.PointerName,
                    item.ObjectId,
                    item.AreaNo,
                    item.ByteOffset,
                    item.ByteSize));
        }

        Save(document, tsprojPath);
    }

    public void EnsureTaskPouOid(string tsprojPath, EnsureTaskPouOidRequest request)
    {
        ValidateRequiredText(request.PlcProjectName, nameof(request.PlcProjectName));
        ValidateRequiredText(request.PlcInstanceName, nameof(request.PlcInstanceName));
        if (request.Priority < 0)
        {
            throw new InvalidOperationException("TaskPouOid Priority must be greater than or equal to zero.");
        }

        if (!string.IsNullOrWhiteSpace(request.ObjectId))
        {
            ValidateObjectIdText(request.ObjectId, nameof(request.ObjectId));
        }

        XDocument document = Load(tsprojPath);
        XElement plcInstance = FindPlcInstance(document, request.PlcProjectName, request.PlcInstanceName);
        XElement taskPouOids = GetOrCreateChildElement(plcInstance, "TaskPouOids");
        string priorityText = request.Priority.ToString(CultureInfo.InvariantCulture);
        XElement taskPouOid = taskPouOids.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "TaskPouOid" &&
                string.Equals(GetAttributeValue(element, "Prio"), priorityText, StringComparison.OrdinalIgnoreCase))
            ?? taskPouOids.Elements().FirstOrDefault(element => element.Name.LocalName == "TaskPouOid")
            ?? AddChild(taskPouOids, "TaskPouOid");

        taskPouOid.SetAttributeValue("Prio", priorityText);
        if (string.IsNullOrWhiteSpace(request.ObjectId))
        {
            taskPouOid.SetAttributeValue("OTCID", null);
        }
        else
        {
            taskPouOid.SetAttributeValue("OTCID", request.ObjectId);
        }

        Save(document, tsprojPath);
    }

    public void EnsureInitSymbol(string tsprojPath, EnsureInitSymbolRequest request)
    {
        ValidateRequiredText(request.PlcProjectName, nameof(request.PlcProjectName));
        ValidateRequiredText(request.PlcInstanceName, nameof(request.PlcInstanceName));
        ValidateRequiredText(request.SymbolName, nameof(request.SymbolName));
        ValidateRequiredText(request.TypeName, nameof(request.TypeName));
        ValidateRequiredText(request.AreaNo, nameof(request.AreaNo));
        ValidateObjectIdText(request.ObjectId, nameof(request.ObjectId));

        XDocument document = Load(tsprojPath);
        XElement plcInstance = FindPlcInstance(document, request.PlcProjectName, request.PlcInstanceName);
        XElement initSymbols = GetOrCreateChildElement(plcInstance, "InitSymbols");
        XElement initSymbol = initSymbols.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "InitSymbol" &&
                string.Equals(GetChildElementValue(element, "Name"), request.SymbolName, StringComparison.OrdinalIgnoreCase))
            ?? AddChild(initSymbols, "InitSymbol");

        SetOrCreateChildElementValue(initSymbol, "Name", request.SymbolName);
        XElement typeElement = GetOrCreateChildElement(initSymbol, "Type");
        typeElement.Value = request.TypeName;
        typeElement.SetAttributeValue("GUID", request.TypeGuid);
        SetOrCreateChildElementValue(initSymbol, "AreaNo", request.AreaNo);
        SetOrCreateChildElementValue(initSymbol, "Data", ConvertObjectIdToInitSymbolData(request.ObjectId));

        Save(document, tsprojPath);
    }

    public void EnsureMappingLink(string tsprojPath, EnsureMappingLinkRequest request)
    {
        ValidateMappingLinkRequest(request);
        XDocument document = Load(tsprojPath);
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        XElement mappings = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Mappings")
            ?? AddChild(root, "Mappings");

        XElement ownerA = mappings.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "OwnerA" &&
                string.Equals(GetAttributeValue(element, "Name"), request.OwnerAName, StringComparison.OrdinalIgnoreCase))
            ?? AddOwnerElement(mappings, "OwnerA", request.OwnerAName);

        XElement ownerB = ownerA.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "OwnerB" &&
                string.Equals(GetAttributeValue(element, "Name"), request.OwnerBName, StringComparison.OrdinalIgnoreCase))
            ?? AddOwnerElement(ownerA, "OwnerB", request.OwnerBName);

        XElement? existingLink = ownerB.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Link" &&
            string.Equals(GetAttributeValue(element, "VarA"), request.VarA, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetAttributeValue(element, "VarB"), request.VarB, StringComparison.OrdinalIgnoreCase));

        if (existingLink is null)
        {
            XElement link = AddChild(ownerB, "Link");
            link.SetAttributeValue("VarA", request.VarA);
            link.SetAttributeValue("VarB", request.VarB);
        }

        Save(document, tsprojPath);
    }

    private static void ValidateMappingLinkRequest(EnsureMappingLinkRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerAName))
        {
            throw new InvalidOperationException("Mapping OwnerAName must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.OwnerBName))
        {
            throw new InvalidOperationException("Mapping OwnerBName must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.VarA))
        {
            throw new InvalidOperationException("Mapping VarA must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.VarB))
        {
            throw new InvalidOperationException("Mapping VarB must not be empty.");
        }
    }

    public void EnsureIoTaskImage(string tsprojPath, EnsureIoTaskImageRequest request)
    {
        ValidateRequiredText(request.TaskName, nameof(request.TaskName));
        ValidateRequiredText(request.InstanceName, nameof(request.InstanceName));
        ValidateRequiredText(request.PointerName, nameof(request.PointerName));
        if (request.ImageId <= 0)
        {
            throw new InvalidOperationException("Image Id must be greater than zero.");
        }

        if (request.SizeIn < 0 || request.SizeOut < 0)
        {
            throw new InvalidOperationException("Image SizeIn and SizeOut must be greater than or equal to zero.");
        }

        if (request.InputRealCount <= 0)
        {
            throw new InvalidOperationException("InputRealCount must be greater than zero.");
        }

        if (request.OutputByteCount <= 0)
        {
            throw new InvalidOperationException("OutputByteCount must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(request.ImageObjectId))
        {
            ValidateObjectIdText(request.ImageObjectId, nameof(request.ImageObjectId));
        }

        XDocument document = Load(tsprojPath);
        XElement task = FindTaskByName(document, request.TaskName);
        task.SetAttributeValue("IoAtBegin", request.IoAtBegin.HasValue ? (request.IoAtBegin.Value ? "true" : "false") : null);

        foreach (XElement child in task.Elements().Where(element =>
                     element.Name.LocalName == "Vars" ||
                     element.Name.LocalName == "Image").ToList())
        {
            child.Remove();
        }

        XNamespace defaultNamespace = task.GetDefaultNamespace();
        if (request.EnsureDefaultTaskVariables)
        {
            task.Add(CreateTaskVars(
                defaultNamespace,
                varGrpType: 1,
                insertType: 1,
                groupName: "Inputs",
                baseVarName: "Var ",
                typeName: "REAL",
                count: request.InputRealCount,
                bitStride: 32,
                externalAddressStride: 4,
                firstExternalAddress: null,
                startIndex: 1));

            task.Add(CreateTaskVars(
                defaultNamespace,
                varGrpType: 2,
                insertType: 1,
                groupName: "Outputs",
                baseVarName: "Var ",
                typeName: "BYTE",
                count: request.OutputByteCount,
                bitStride: 8,
                externalAddressStride: 1,
                firstExternalAddress: null,
                startIndex: request.InputRealCount + 1));
        }

        XElement imageElement = AddChild(task, "Image");
        imageElement.SetAttributeValue("Id", request.ImageId.ToString(CultureInfo.InvariantCulture));
        imageElement.SetAttributeValue("AddrType", "1");
        imageElement.SetAttributeValue("ImageType", "1");
        imageElement.SetAttributeValue("SizeIn", request.SizeIn.ToString(CultureInfo.InvariantCulture));
        imageElement.SetAttributeValue("SizeOut", request.SizeOut.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(imageElement, "Name", "Image");

        string imageObjectId = string.IsNullOrWhiteSpace(request.ImageObjectId)
            ? DeriveIoTaskImageObjectId(request.ImageId)
            : request.ImageObjectId;

        XElement instance = FindInstance(document, request.InstanceName);
        XElement tmcDesc = GetOrCreateTmcDesc(instance);
        XElement pointers = GetOrCreateChildElement(tmcDesc, "InterfacePointerValues");
        XElement value = FindNamedValue(pointers, request.PointerName) ?? CreateNamedValue(pointers, request.PointerName);
        SetOrCreateChildElementValue(value, "OTCID", imageObjectId);

        Save(document, tsprojPath);
    }

    public void EnsureParameterValue(string tsprojPath, EnsureParameterValueRequest request)
    {
        XDocument document = Load(tsprojPath);
        EnsureParameterValueInDocument(document, request);
        Save(document, tsprojPath);
    }

    public void EnsureInterfacePointerValue(string tsprojPath, EnsureInterfacePointerValueRequest request)
    {
        XDocument document = Load(tsprojPath);
        EnsureInterfacePointerValueInDocument(document, request);
        Save(document, tsprojPath);
    }

    public void EnsureDataPointerValue(string tsprojPath, EnsureDataPointerValueRequest request)
    {
        XDocument document = Load(tsprojPath);
        EnsureDataPointerValueInDocument(document, request);
        Save(document, tsprojPath);
    }

    public void MergeNamedElementFragment(string tsprojPath, MergeNamedElementFragmentRequest request)
    {
        ValidateMergeFragmentDocumentation(request);

        XDocument document = Load(tsprojPath);
        XElement parent = FindSingleOrCreateContainer(document.Root!, request.ParentElementName);
        XElement fragment = XElement.Parse(request.FragmentXml);
        XElement? candidate = ResolveExistingElement(parent, fragment, request.MatchElementName, request.MatchNameValue);

        if (candidate is not null)
        {
            if (request.ReplaceExisting)
            {
                candidate.ReplaceWith(new XElement(fragment));
            }
        }
        else
        {
            parent.Add(new XElement(fragment));
        }

        Save(document, tsprojPath);
    }

    public static string ConvertObjectIdToInitSymbolData(string objectId)
    {
        uint parsed = ParseObjectId(objectId);
        byte[] bytes = BitConverter.GetBytes(parsed);
        StringBuilder builder = new(bytes.Length * 2);
        foreach (byte value in bytes)
        {
            builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public static string DeriveIoTaskImageObjectId(int imageId)
    {
        if (imageId <= 0)
        {
            throw new InvalidOperationException("Image Id must be greater than zero.");
        }

        uint imageObjectId = 0x03040000u | ((uint)imageId << 4);
        return "#x" + imageObjectId.ToString("X8", CultureInfo.InvariantCulture);
    }

    public static string DeriveNextObjectId(string currentObjectId, uint increment = 1)
    {
        uint parsed = ParseObjectId(currentObjectId);
        return "#x" + (parsed + increment).ToString("X8", CultureInfo.InvariantCulture);
    }

    private static XDocument Load(string tsprojPath) =>
        XDocument.Load(tsprojPath, LoadOptions.PreserveWhitespace);

    private static void Save(XDocument document, string tsprojPath) =>
        document.Save(tsprojPath);

    private static void ApplyMutationPlanInternal(XDocument document, ApplyTsprojMutationPlanRequest request)
    {
        foreach (TsprojElementUpsertRequest item in request.ElementUpserts ?? Array.Empty<TsprojElementUpsertRequest>())
        {
            ApplyElementUpsert(document, item);
        }

        foreach (TsprojFragmentUpsertRequest item in request.FragmentUpserts ?? Array.Empty<TsprojFragmentUpsertRequest>())
        {
            ApplyFragmentUpsert(document, item);
        }
    }

    private static void ApplyElementUpsert(XDocument document, TsprojElementUpsertRequest request)
    {
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        XElement parent = ResolveOrCreatePath(root, request.ParentPath);
        XElement target = FindOrCreateTargetElement(parent, request.ElementName, request.MatchNameValue);

        foreach (TsprojXmlAttribute item in request.Attributes ?? Array.Empty<TsprojXmlAttribute>())
        {
            XAttribute? existing = target.Attribute(item.Name);
            if (existing is null)
            {
                target.SetAttributeValue(item.Name, item.Value);
                continue;
            }

            if (string.Equals(existing.Value, item.Value, StringComparison.Ordinal))
            {
                continue;
            }

            switch (request.ConflictPolicy)
            {
                case TsprojMutationConflictPolicy.ReplaceExisting:
                    existing.Value = item.Value;
                    break;
                case TsprojMutationConflictPolicy.KeepExisting:
                    break;
                case TsprojMutationConflictPolicy.FailOnConflict:
                    throw new InvalidOperationException(
                        $"Attribute conflict on '{target.Name.LocalName}@{item.Name}': existing='{existing.Value}', requested='{item.Value}'.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(request.ConflictPolicy), request.ConflictPolicy, "Unsupported conflict policy.");
            }
        }

        foreach (TsprojXmlChildValue item in request.ChildValues ?? Array.Empty<TsprojXmlChildValue>())
        {
            XElement child = GetOrCreateChildElement(target, item.ElementName);
            if (string.Equals(child.Value, item.Value, StringComparison.Ordinal))
            {
                continue;
            }

            switch (request.ConflictPolicy)
            {
                case TsprojMutationConflictPolicy.ReplaceExisting:
                    child.Value = item.Value;
                    break;
                case TsprojMutationConflictPolicy.KeepExisting:
                    if (string.IsNullOrWhiteSpace(child.Value))
                    {
                        child.Value = item.Value;
                    }

                    break;
                case TsprojMutationConflictPolicy.FailOnConflict:
                    if (string.IsNullOrWhiteSpace(child.Value))
                    {
                        child.Value = item.Value;
                        break;
                    }

                    throw new InvalidOperationException(
                        $"Child element conflict on '{target.Name.LocalName}/{item.ElementName}': existing='{child.Value}', requested='{item.Value}'.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(request.ConflictPolicy), request.ConflictPolicy, "Unsupported conflict policy.");
            }
        }
    }

    private static void ApplyFragmentUpsert(XDocument document, TsprojFragmentUpsertRequest request)
    {
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        XElement parent = ResolveOrCreatePath(root, request.ParentPath);
        XNamespace defaultNamespace = parent.GetDefaultNamespace();
        XElement normalizedFragment = CloneWithNamespace(XElement.Parse(request.FragmentXml), defaultNamespace);

        string matchElementName = request.MatchElementName ?? normalizedFragment.Name.LocalName;
        string? matchNameValue = request.MatchNameValue ?? GetChildElementValue(normalizedFragment, "Name");
        XElement? existing = FindSingleElement(parent, matchElementName, matchNameValue);

        if (existing is null)
        {
            parent.Add(new XElement(normalizedFragment));
            return;
        }

        switch (request.ConflictPolicy)
        {
            case TsprojMutationConflictPolicy.ReplaceExisting:
                existing.ReplaceWith(new XElement(normalizedFragment));
                break;
            case TsprojMutationConflictPolicy.KeepExisting:
                break;
            case TsprojMutationConflictPolicy.FailOnConflict:
                if (!XNode.DeepEquals(existing, normalizedFragment))
                {
                    throw new InvalidOperationException(
                        $"Fragment conflict on '{matchElementName}' with Name='{matchNameValue ?? "<null>"}'.");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request.ConflictPolicy), request.ConflictPolicy, "Unsupported conflict policy.");
        }
    }

    private static XElement FindTaskByName(XDocument document, string taskName) =>
        document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Task" &&
            string.Equals(GetChildElementValue(element, "Name"), taskName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Task '{taskName}' was not found in the .tsproj.");

    private static XElement FindPlcProject(XDocument document, string plcProjectName) =>
        document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Project" &&
            element.Parent?.Name.LocalName == "Plc" &&
            string.Equals(GetAttributeValue(element, "Name") ?? GetChildElementValue(element, "Name"), plcProjectName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"PLC project '{plcProjectName}' was not found in the .tsproj.");

    private static XElement FindCppProject(XDocument document, string cppProjectName) =>
        document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Project" &&
            element.Parent?.Name.LocalName == "Cpp" &&
            string.Equals(GetAttributeValue(element, "Name") ?? GetChildElementValue(element, "Name"), cppProjectName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"C++ project '{cppProjectName}' was not found in the .tsproj.");

    private static XElement FindOrCreatePlcProject(XDocument document, string plcProjectName)
    {
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        XElement? plc = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Plc" &&
            element.Parent?.Name.LocalName == "Project");
        plc ??= root.Elements().FirstOrDefault(element => element.Name.LocalName == "Plc");

        if (plc is null)
        {
            XElement? topLevelProject = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Project");
            XElement parent = topLevelProject ?? root;
            plc = AddChild(parent, "Plc");
        }

        XElement plcProject = plc.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Project" &&
                string.Equals(GetAttributeValue(element, "Name") ?? GetChildElementValue(element, "Name"), plcProjectName, StringComparison.OrdinalIgnoreCase))
            ?? AddChild(plc, "Project");

        plcProject.SetAttributeValue("Name", plcProjectName);
        return plcProject;
    }

    private static XElement FindPlcInstance(XDocument document, string plcProjectName, string plcInstanceName)
    {
        XElement plcProject = FindPlcProject(document, plcProjectName);

        return plcProject.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Instance" &&
                string.Equals(GetChildElementValue(element, "Name"), plcInstanceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"PLC instance '{plcInstanceName}' was not found in project '{plcProjectName}'.");
    }

    private static XElement ResolveOrCreatePlcContext(XElement contexts, string plcTaskName, int contextId)
    {
        string contextIdText = contextId.ToString(CultureInfo.InvariantCulture);
        XElement? context = contexts.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Context" &&
            string.Equals(GetChildElementValue(element, "Name"), plcTaskName, StringComparison.OrdinalIgnoreCase));
        context ??= contexts.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Context" &&
            string.Equals(GetChildElementValue(element, "Id"), contextIdText, StringComparison.OrdinalIgnoreCase));
        context ??= contexts.Elements().FirstOrDefault(element => element.Name.LocalName == "Context");
        return context ?? AddChild(contexts, "Context");
    }

    private static XElement FindTopLevelProject(XDocument document)
    {
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        return root.Elements().FirstOrDefault(element => element.Name.LocalName == "Project")
            ?? throw new InvalidOperationException("Project node was not found in the .tsproj root.");
    }

    private static XElement FindOrCreateCanonicalTasksContainer(XDocument document)
    {
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        XElement project = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Project")
            ?? AddChild(root, "Project");
        XElement system = project.Elements().FirstOrDefault(element => element.Name.LocalName == "System")
            ?? AddChild(project, "System");
        XElement tasks = system.Elements().FirstOrDefault(element => element.Name.LocalName == "Tasks")
            ?? AddChild(system, "Tasks");
        return tasks;
    }

    private static void EnsureParameterValueInDocument(XDocument document, EnsureParameterValueRequest request)
    {
        ValidateRequiredText(request.InstanceName, nameof(request.InstanceName));
        ValidateRequiredText(request.ParameterName, nameof(request.ParameterName));

        XElement instance = FindInstance(document, request.InstanceName);
        XElement parameters = GetOrCreateChildElement(GetOrCreateTmcDesc(instance), "ParameterValues");
        XElement value = FindNamedValue(parameters, request.ParameterName) ?? CreateNamedValue(parameters, request.ParameterName);

        if (request.ValueText is not null)
        {
            SetOrCreateChildElementValue(value, "Value", request.ValueText);
        }

        if (request.EnumText is not null)
        {
            SetOrCreateChildElementValue(value, "EnumText", request.EnumText);
        }

        if (request.StringText is not null)
        {
            SetOrCreateChildElementValue(value, "String", request.StringText);
        }
    }

    private static void EnsureInterfacePointerValueInDocument(XDocument document, EnsureInterfacePointerValueRequest request)
    {
        ValidateRequiredText(request.InstanceName, nameof(request.InstanceName));
        ValidateRequiredText(request.PointerName, nameof(request.PointerName));
        ValidateObjectIdText(request.ObjectId, nameof(request.ObjectId));

        XElement instance = FindInstance(document, request.InstanceName);
        XElement pointers = GetOrCreateChildElement(GetOrCreateTmcDesc(instance), "InterfacePointerValues");
        XElement value = FindNamedValue(pointers, request.PointerName) ?? CreateNamedValue(pointers, request.PointerName);
        SetOrCreateChildElementValue(value, "OTCID", request.ObjectId);
    }

    private static void EnsureDataPointerValueInDocument(XDocument document, EnsureDataPointerValueRequest request)
    {
        ValidateDataPointerRequest(request);
        XElement instance = FindInstance(document, request.InstanceName);
        XElement pointers = GetOrCreateChildElement(GetOrCreateTmcDesc(instance), "DataPointerValues");
        XElement value = FindNamedValue(pointers, request.PointerName) ?? CreateNamedValue(pointers, request.PointerName);
        SetOrCreateChildElementValue(value, "OTCID", request.ObjectId);
        SetOrCreateChildElementValue(value, "AreaNo", request.AreaNo.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(value, "ByteOffs", request.ByteOffset.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(value, "ByteSize", request.ByteSize.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidateDataPointerRequest(EnsureDataPointerValueRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceName))
        {
            throw new InvalidOperationException("Data pointer instance name must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.PointerName))
        {
            throw new InvalidOperationException("Data pointer name must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.ObjectId))
        {
            throw new InvalidOperationException("Data pointer ObjectId must not be empty.");
        }

        ValidateObjectIdText(request.ObjectId, nameof(request.ObjectId));

        if (request.AreaNo < 0)
        {
            throw new InvalidOperationException("Data pointer AreaNo must be greater than or equal to 0.");
        }

        if (request.ByteOffset < 0)
        {
            throw new InvalidOperationException("Data pointer ByteOffset must be greater than or equal to 0.");
        }

        if (request.ByteSize <= 0)
        {
            throw new InvalidOperationException("Data pointer ByteSize must be greater than 0.");
        }
    }

    private static XElement AddOwnerElement(XElement parent, string localName, string name)
    {
        XElement created = AddChild(parent, localName);
        created.SetAttributeValue("Name", name);
        return created;
    }

    private static string? GetAttributeValue(XElement element, string attributeName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == attributeName)?.Value;

    private static XElement ParseAndNormalizeSectionFragment(string fragmentXml, XNamespace targetNamespace, string expectedSectionName)
    {
        XElement parsed = XElement.Parse(fragmentXml);
        if (!string.Equals(parsed.Name.LocalName, expectedSectionName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected a '{expectedSectionName}' fragment but received '{parsed.Name.LocalName}'.");
        }

        return CloneWithNamespace(parsed, targetNamespace);
    }

    private static XElement CreateTaskVars(
        XNamespace defaultNamespace,
        int varGrpType,
        int insertType,
        string groupName,
        string baseVarName,
        string typeName,
        int count,
        int bitStride,
        int externalAddressStride,
        int? firstExternalAddress,
        int startIndex)
    {
        XElement varsElement = new(
            defaultNamespace + "Vars",
            new XAttribute("VarGrpType", varGrpType.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("InsertType", insertType.ToString(CultureInfo.InvariantCulture)),
            new XElement(defaultNamespace + "Name", groupName));

        for (int offset = 0; offset < count; offset++)
        {
            int varIndex = startIndex + offset;
            XElement varElement = new(
                defaultNamespace + "Var",
                new XElement(defaultNamespace + "Name", baseVarName + varIndex.ToString(CultureInfo.InvariantCulture)),
                new XElement(defaultNamespace + "Type", typeName));

            if (offset > 0)
            {
                varElement.Add(new XElement(defaultNamespace + "BitOffs", (offset * bitStride).ToString(CultureInfo.InvariantCulture)));
                int externalAddress = (firstExternalAddress ?? externalAddressStride) + ((offset - 1) * externalAddressStride);
                varElement.Add(new XElement(defaultNamespace + "ExternalAddress", externalAddress.ToString(CultureInfo.InvariantCulture)));
            }

            varsElement.Add(varElement);
        }

        return varsElement;
    }

    private static XElement FindInstance(XDocument document, string instanceName) =>
        document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Instance" &&
            string.Equals(GetChildElementValue(element, "Name"), instanceName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Instance '{instanceName}' was not found in the .tsproj.");

    private static XElement GetOrCreateTmcDesc(XElement instance) =>
        instance.Elements().FirstOrDefault(element => element.Name.LocalName == "TmcDesc")
        ?? AddChild(instance, "TmcDesc");

    private static XElement FindOrCreateContainer(XElement root, string localName) =>
        root.Descendants().FirstOrDefault(element => element.Name.LocalName == localName)
        ?? AddChild(root, localName);

    private static XElement FindSingleOrCreateContainer(XElement root, string localName)
    {
        List<XElement> matches = root.Descendants()
            .Where(element => element.Name.LocalName == localName)
            .ToList();

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Parent element '{localName}' is ambiguous in the .tsproj. Use a dedicated API or a generic upsert with ParentPath instead of tsproj.merge-fragment.");
        }

        return AddChild(root, localName);
    }

    private static void ValidateMergeFragmentDocumentation(MergeNamedElementFragmentRequest request)
    {
        ValidateRequiredText(request.ParentElementName, nameof(request.ParentElementName));
        ValidateRequiredText(request.FragmentXml, nameof(request.FragmentXml));
        if (string.IsNullOrWhiteSpace(request.FragmentSource) ||
            string.IsNullOrWhiteSpace(request.TargetParentPath) ||
            string.IsNullOrWhiteSpace(request.FieldMeaning) ||
            string.IsNullOrWhiteSpace(request.VerificationEvidence))
        {
            throw new InvalidOperationException(
                "tsproj.merge-fragment requires FragmentSource, TargetParentPath, FieldMeaning, and VerificationEvidence. Use a dedicated .tsproj API when one exists.");
        }
    }

    private static void ValidateRequiredText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName} must not be empty.");
        }
    }

    private static void ValidateObjectIdText(string? value, string fieldName)
    {
        ValidateRequiredText(value, fieldName);
        _ = ParseObjectId(value!);
    }

    private static XElement? ResolveExistingElement(
        XElement parent,
        XElement fragment,
        string? matchElementName,
        string? matchNameValue)
    {
        string targetElementName = matchElementName ?? fragment.Name.LocalName;
        string? targetNameValue = matchNameValue ?? GetChildElementValue(fragment, "Name");

        return parent.Elements().FirstOrDefault(element =>
            element.Name.LocalName == targetElementName &&
            (targetNameValue is null || string.Equals(GetChildElementValue(element, "Name"), targetNameValue, StringComparison.OrdinalIgnoreCase)));
    }

    private static XElement? FindNamedValue(XElement container, string name) =>
        container.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Value" &&
            string.Equals(GetChildElementValue(element, "Name"), name, StringComparison.OrdinalIgnoreCase));

    private static XElement CreateNamedValue(XElement container, string name)
    {
        XElement value = AddChild(container, "Value");
        AddChild(value, "Name").Value = name;
        return value;
    }

    private static XElement AddChild(XElement parent, string localName)
    {
        XElement child = new(parent.GetDefaultNamespace() + localName);
        parent.Add(child);
        return child;
    }

    private static XElement GetOrCreateChildElement(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName) ?? AddChild(parent, localName);

    private static void SetOrCreateChildElementValue(XElement parent, string localName, string value) =>
        GetOrCreateChildElement(parent, localName).Value = value;

    private static string? GetChildElementValue(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;

    private static uint ParseObjectId(string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            throw new InvalidOperationException("ObjectId cannot be empty.");
        }

        string raw = objectId.Trim();
        if (raw.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[2..];
        }

        if (!uint.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed))
        {
            throw new FormatException($"Unable to parse ObjectId '{objectId}'.");
        }

        return parsed;
    }

    private static XElement ResolveOrCreatePath(XElement root, IReadOnlyList<TsprojPathSegment> path)
    {
        if (path is null || path.Count == 0)
        {
            return root;
        }

        XElement current = root;
        foreach (TsprojPathSegment segment in path)
        {
            if (string.IsNullOrWhiteSpace(segment.ElementName))
            {
                throw new InvalidOperationException("Path segment ElementName cannot be empty.");
            }

            current = FindOrCreateDirectChild(current, segment.ElementName, segment.NameValue);
        }

        return current;
    }

    private static XElement FindOrCreateDirectChild(XElement parent, string elementName, string? nameValue)
    {
        XElement? existing = FindSingleElement(parent, elementName, nameValue, directChildrenOnly: true);
        if (existing is not null)
        {
            return existing;
        }

        XElement created = AddChild(parent, elementName);
        if (!string.IsNullOrWhiteSpace(nameValue))
        {
            SetOrCreateChildElementValue(created, "Name", nameValue);
        }

        return created;
    }

    private static XElement FindOrCreateTargetElement(XElement parent, string elementName, string? matchNameValue)
    {
        XElement? existing = FindSingleElement(parent, elementName, matchNameValue, directChildrenOnly: true);
        if (existing is not null)
        {
            return existing;
        }

        XElement created = AddChild(parent, elementName);
        if (!string.IsNullOrWhiteSpace(matchNameValue))
        {
            SetOrCreateChildElementValue(created, "Name", matchNameValue);
        }

        return created;
    }

    private static XElement? FindSingleElement(
        XElement parent,
        string elementName,
        string? matchNameValue,
        bool directChildrenOnly = true)
    {
        IEnumerable<XElement> candidates = directChildrenOnly
            ? parent.Elements()
            : parent.Descendants();

        List<XElement> matches = candidates.Where(element =>
                element.Name.LocalName == elementName &&
                (matchNameValue is null || string.Equals(GetChildElementValue(element, "Name"), matchNameValue, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matches.Count <= 1)
        {
            return matches.Count == 0 ? null : matches[0];
        }

        throw new InvalidOperationException(
            $"Ambiguous XML match for element '{elementName}' with Name='{matchNameValue ?? "<null>"}' under '{parent.Name.LocalName}'.");
    }

    private static XElement CloneWithNamespace(XElement source, XNamespace targetNamespace)
    {
        XName qualifiedName = string.IsNullOrEmpty(targetNamespace.NamespaceName)
            ? XName.Get(source.Name.LocalName)
            : targetNamespace + source.Name.LocalName;

        XElement clone = new(qualifiedName);
        foreach (XAttribute attribute in source.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                clone.SetAttributeValue(attribute.Name, attribute.Value);
            }
        }

        foreach (XNode node in source.Nodes())
        {
            switch (node)
            {
                case XElement childElement:
                    clone.Add(CloneWithNamespace(childElement, targetNamespace));
                    break;
                default:
                    clone.Add(node);
                    break;
            }
        }

        return clone;
    }
}
