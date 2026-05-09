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

    public RefreshCppInstanceTmcDescResult RefreshCppInstanceTmcDesc(string tsprojPath, RefreshCppInstanceTmcDescRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ValidateRequiredText(request.CppProjectName, nameof(request.CppProjectName));
        ValidateRequiredText(request.ProjectTmcPath, nameof(request.ProjectTmcPath));
        if (request.Instances is null || request.Instances.Count == 0)
        {
            throw new InvalidOperationException("RefreshCppInstanceTmcDesc requires at least one instance item.");
        }

        foreach (CppInstanceTmcDescRefreshItem item in request.Instances)
        {
            ValidateRequiredText(item.InstanceName, nameof(item.InstanceName));
            ValidateRequiredText(item.ModuleClassName, nameof(item.ModuleClassName));
        }

        string resolvedTmcPath = Path.GetFullPath(request.ProjectTmcPath);
        if (!File.Exists(resolvedTmcPath))
        {
            throw new FileNotFoundException("Project TMC file was not found.", resolvedTmcPath);
        }

        XDocument document = Load(tsprojPath);
        XDocument tmcDocument = XDocument.Load(resolvedTmcPath);
        Dictionary<string, XElement> moduleByName = ReadTmcModulesByName(tmcDocument);
        XElement cppProject = FindCppProject(document, request.CppProjectName);
        List<string> errors = [];
        int refreshedCount = 0;

        foreach (CppInstanceTmcDescRefreshItem item in request.Instances)
        {
            if (!moduleByName.TryGetValue(item.ModuleClassName, out XElement? module))
            {
                string error = $"Module '{item.ModuleClassName}' was not found inside '{resolvedTmcPath}'.";
                if (request.FailIfMissingModule)
                {
                    errors.Add(error);
                    continue;
                }

                continue;
            }

            XElement? instance = cppProject.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Instance" &&
                string.Equals(GetChildElementValue(element, "Name"), item.InstanceName, StringComparison.OrdinalIgnoreCase));
            if (instance is null)
            {
                errors.Add($"Instance '{item.InstanceName}' was not found in C++ project '{request.CppProjectName}'.");
                continue;
            }

            XElement? oldTmcDesc = instance.Elements().FirstOrDefault(element => element.Name.LocalName == "TmcDesc");
            XElement newTmcDesc = CreateTmcDescFromModule(
                module,
                oldTmcDesc,
                item,
                request.PreserveValueSections,
                request.PreserveContextValues);

            if (oldTmcDesc is null)
            {
                instance.Add(newTmcDesc);
            }
            else
            {
                oldTmcDesc.ReplaceWith(newTmcDesc);
            }

            if (!string.IsNullOrWhiteSpace(item.ClassFactoryId))
            {
                instance.SetAttributeValue("ClassFactoryId", item.ClassFactoryId);
            }
            else if (newTmcDesc.Attribute("ClassFactoryId") is XAttribute classFactoryId)
            {
                instance.SetAttributeValue("ClassFactoryId", classFactoryId.Value);
            }

            refreshedCount++;
        }

        if (request.ImportDataTypesFromTmc)
        {
            ImportDataTypesFromTmc(document, tmcDocument);
        }

        if (errors.Count > 0)
        {
            return new RefreshCppInstanceTmcDescResult(
                false,
                Path.GetFullPath(tsprojPath),
                refreshedCount,
                errors,
                $"C++ instance TmcDesc refresh failed with {errors.Count} error(s).");
        }

        Save(document, tsprojPath);
        return new RefreshCppInstanceTmcDescResult(
            true,
            Path.GetFullPath(tsprojPath),
            refreshedCount,
            Array.Empty<string>(),
            $"Refreshed {refreshedCount} C++ instance TmcDesc item(s).");
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

        XDocument document = Load(tsprojPath);
        XElement task = FindTaskByName(document, request.TaskName);
        if (request.Affinity is not null)
        {
            ValidateRequiredText(request.Affinity, nameof(request.Affinity));
            task.SetAttributeValue("Affinity", request.Affinity);
        }

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

    public void SetCppInstanceMetadata(string tsprojPath, SetCppInstanceMetadataRequest request)
    {
        ValidateRequiredText(request.InstanceName, nameof(request.InstanceName));
        if (!string.IsNullOrWhiteSpace(request.ObjectId))
        {
            ValidateObjectIdText(request.ObjectId, nameof(request.ObjectId));
        }

        XDocument document = Load(tsprojPath);
        XElement instance = FindInstance(document, request.InstanceName);

        if (request.Disabled.HasValue)
        {
            instance.SetAttributeValue("Disabled", request.Disabled.Value ? "true" : null);
        }

        if (request.KeepUnrestoredLinks is not null)
        {
            instance.SetAttributeValue("KeepUnrestoredLinks", string.IsNullOrWhiteSpace(request.KeepUnrestoredLinks) ? null : request.KeepUnrestoredLinks);
        }

        if (request.ClassFactoryId is not null)
        {
            instance.SetAttributeValue("ClassFactoryId", string.IsNullOrWhiteSpace(request.ClassFactoryId) ? null : request.ClassFactoryId);
        }

        if (request.ObjectId is not null)
        {
            instance.SetAttributeValue("Id", string.IsNullOrWhiteSpace(request.ObjectId) ? null : request.ObjectId);
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

    public EnsureIoSectionResult EnsureIoSection(string tsprojPath, EnsureIoSectionRequest request)
    {
        XDocument document = Load(tsprojPath);
        XElement project = FindTopLevelProject(document);
        XElement? io = project.Elements().FirstOrDefault(element => element.Name.LocalName == "Io");
        bool created = false;
        if (io is null)
        {
            io = AddChild(project, "Io");
            created = true;
        }

        int deviceCount = io.Elements().Count(element => element.Name.LocalName == "Device");
        Save(document, tsprojPath);
        return new EnsureIoSectionResult(created, deviceCount, Path.GetFullPath(tsprojPath));
    }

    public void EnsureIoDevice(string tsprojPath, EnsureIoDeviceRequest request)
    {
        XDocument document = Load(tsprojPath);
        EnsureIoDeviceInDocument(document, request);
        Save(document, tsprojPath);
    }

    public void EnsureEthercatBox(string tsprojPath, EnsureEthercatBoxRequest request)
    {
        XDocument document = Load(tsprojPath);
        EnsureEthercatBoxInDocument(document, request);
        Save(document, tsprojPath);
    }

    public void EnsureIoPdo(string tsprojPath, EnsureIoPdoRequest request)
    {
        XDocument document = Load(tsprojPath);
        EnsureIoPdoInDocument(document, request);
        Save(document, tsprojPath);
    }

    public void EnsureIoBoxImage(string tsprojPath, EnsureIoBoxImageRequest request)
    {
        XDocument document = Load(tsprojPath);
        EnsureIoBoxImageInDocument(document, request);
        Save(document, tsprojPath);
    }

    public void EnsureMappingInfo(string tsprojPath, EnsureMappingInfoRequest request)
    {
        XDocument document = Load(tsprojPath);
        EnsureMappingInfoInDocument(document, request);
        Save(document, tsprojPath);
    }

    public void EnsureIoMappingLink(string tsprojPath, EnsureIoMappingLinkRequest request)
    {
        XDocument document = Load(tsprojPath);
        EnsureIoMappingLinkInDocument(document, request);
        Save(document, tsprojPath);
    }

    public ApplyIoTopologyPlanResult ApplyIoTopologyPlan(string tsprojPath, ApplyIoTopologyPlanRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        XDocument document = Load(tsprojPath);
        if (request.EnsureIoSection)
        {
            _ = FindOrCreateProjectIo(document);
        }

        foreach (EnsureIoDeviceRequest item in request.Devices ?? Array.Empty<EnsureIoDeviceRequest>())
        {
            EnsureIoDeviceInDocument(document, item);
        }

        foreach (EnsureEthercatBoxRequest item in request.Boxes ?? Array.Empty<EnsureEthercatBoxRequest>())
        {
            EnsureEthercatBoxInDocument(document, item);
        }

        foreach (EnsureIoBoxImageRequest item in request.BoxImages ?? Array.Empty<EnsureIoBoxImageRequest>())
        {
            EnsureIoBoxImageInDocument(document, item);
        }

        foreach (EnsureIoPdoRequest item in request.Pdos ?? Array.Empty<EnsureIoPdoRequest>())
        {
            EnsureIoPdoInDocument(document, item);
        }

        foreach (EnsureMappingInfoRequest item in request.MappingInfos ?? Array.Empty<EnsureMappingInfoRequest>())
        {
            EnsureMappingInfoInDocument(document, item);
        }

        foreach (EnsureIoMappingLinkRequest item in request.Links ?? Array.Empty<EnsureIoMappingLinkRequest>())
        {
            EnsureIoMappingLinkInDocument(document, item);
        }

        Save(document, tsprojPath);
        int deviceCount = request.Devices?.Count ?? 0;
        int boxCount = request.Boxes?.Count ?? 0;
        int pdoCount = request.Pdos?.Count ?? 0;
        int boxImageCount = request.BoxImages?.Count ?? 0;
        int mappingInfoCount = request.MappingInfos?.Count ?? 0;
        int linkCount = request.Links?.Count ?? 0;
        return new ApplyIoTopologyPlanResult(
            true,
            Path.GetFullPath(tsprojPath),
            deviceCount,
            boxCount,
            pdoCount,
            boxImageCount,
            mappingInfoCount,
            linkCount,
            $"Applied IO topology plan: {deviceCount} device(s), {boxCount} box(es), {pdoCount} PDO(s), {mappingInfoCount} MappingInfo item(s), {linkCount} link(s).");
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
        XElement system = FindOrCreateProjectSystem(document);

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

    public void EnsureSystemSettings(string tsprojPath, EnsureSystemSettingsRequest request)
    {
        XDocument document = Load(tsprojPath);
        XElement system = FindOrCreateProjectSystem(document);
        XElement settings = system.Elements().FirstOrDefault(element => element.Name.LocalName == "Settings")
            ?? new XElement(system.GetDefaultNamespace() + "Settings");
        if (settings.Parent is null)
        {
            XElement? tasks = system.Elements().FirstOrDefault(element => element.Name.LocalName == "Tasks");
            if (request.InsertBeforeTasks && tasks is not null)
            {
                tasks.AddBeforeSelf(settings);
            }
            else
            {
                system.Add(settings);
            }
        }

        if (request.CpuId.HasValue)
        {
            if (request.CpuId.Value < 0)
            {
                throw new InvalidOperationException("CpuId must be greater than or equal to zero.");
            }

            XElement cpu = settings.Elements().FirstOrDefault(element => element.Name.LocalName == "Cpu")
                ?? AddChild(settings, "Cpu");
            cpu.SetAttributeValue("CpuId", request.CpuId.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (request.IoIdleTaskPriority.HasValue)
        {
            if (request.IoIdleTaskPriority.Value <= 0)
            {
                throw new InvalidOperationException("IoIdleTaskPriority must be greater than zero.");
            }

            XElement ioIdleTask = settings.Elements().FirstOrDefault(element => element.Name.LocalName == "IoIdleTask")
                ?? AddChild(settings, "IoIdleTask");
            ioIdleTask.SetAttributeValue("Priority", request.IoIdleTaskPriority.Value.ToString(CultureInfo.InvariantCulture));
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
                item.ByteSize,
                item.ArrayIndex));
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
                    item.ByteSize,
                    item.ArrayIndex));
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
        EnsureIoMappingLinkInDocument(
            document,
            new EnsureIoMappingLinkRequest(
                request.OwnerAName,
                request.OwnerBName,
                request.VarA,
                request.VarB));
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

    private static void EnsureIoDeviceInDocument(XDocument document, EnsureIoDeviceRequest request)
    {
        ValidateIoDeviceRequest(request);
        XElement io = FindOrCreateProjectIo(document);
        string deviceIdText = request.DeviceId.ToString(CultureInfo.InvariantCulture);
        XElement device = io.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Device" &&
                string.Equals(GetAttributeValue(element, "Id"), deviceIdText, StringComparison.OrdinalIgnoreCase))
            ?? AddChild(io, "Device");

        device.SetAttributeValue("Id", deviceIdText);
        SetOptionalBoolAttribute(device, "Disabled", request.Disabled, removeWhenFalse: true);
        device.SetAttributeValue("DevType", request.DevType.ToString(CultureInfo.InvariantCulture));
        SetOptionalStringAttribute(device, "DevFlags", request.DevFlags);
        SetOptionalIntAttribute(device, "AmsPort", request.AmsPort);
        SetOptionalStringAttribute(device, "AmsNetId", request.AmsNetId);
        SetOptionalStringAttribute(device, "RemoteName", request.RemoteName);
        SetOptionalIntAttribute(device, "InfoImageId", request.InfoImageId);
        SetOrCreateChildElementValue(device, "Name", request.Name);

        if (request.AddressInfo is not null)
        {
            ApplyIoAddressInfo(device, request.AddressInfo);
        }

        foreach (IoImageDefinition image in request.Images ?? Array.Empty<IoImageDefinition>())
        {
            ApplyIoImageDefinition(device, image);
        }

        ApplyIoRawFragments(device, request.ExtraFragments, $"Project/Io/Device[@Id='{deviceIdText}']");
    }

    private static void EnsureEthercatBoxInDocument(XDocument document, EnsureEthercatBoxRequest request)
    {
        ValidateEthercatBoxRequest(request);
        XElement device = FindIoDevice(document, request.DeviceId);
        XElement parent = request.ParentBoxId.HasValue
            ? FindIoBoxUnderDevice(device, request.ParentBoxId.Value)
            : device;

        string boxIdText = request.BoxId.ToString(CultureInfo.InvariantCulture);
        XElement box = parent.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Box" &&
                string.Equals(GetAttributeValue(element, "Id"), boxIdText, StringComparison.OrdinalIgnoreCase))
            ?? AddChild(parent, "Box");

        box.SetAttributeValue("Id", boxIdText);
        SetOptionalBoolAttribute(box, "Disabled", request.Disabled, removeWhenFalse: true);
        box.SetAttributeValue("BoxType", request.BoxType.ToString(CultureInfo.InvariantCulture));
        SetOptionalStringAttribute(box, "BoxFlags", request.BoxFlags);
        SetOrCreateChildElementValue(box, "Name", request.Name);
        if (request.ImageId.HasValue)
        {
            SetOrCreateChildElementValue(box, "ImageId", request.ImageId.Value.ToString(CultureInfo.InvariantCulture));
        }

        if ((request.EtherCatAttributes?.Count ?? 0) > 0 || (request.EtherCatChildValues?.Count ?? 0) > 0)
        {
            XElement etherCat = GetOrCreateChildElement(box, "EtherCAT");
            ApplyXmlAttributes(etherCat, request.EtherCatAttributes, replaceExisting: true);
            ApplyXmlChildValues(etherCat, request.EtherCatChildValues);
        }

        ApplyIoRawFragments(box, request.ExtraFragments, $"Project/Io/Device[@Id='{request.DeviceId}']/Box[@Id='{boxIdText}']");
    }

    private static void EnsureIoPdoInDocument(XDocument document, EnsureIoPdoRequest request)
    {
        ValidateIoPdoRequest(request);
        XElement device = FindIoDevice(document, request.DeviceId);
        XElement box = FindIoBoxUnderDevice(device, request.BoxId);
        XElement etherCat = GetOrCreateChildElement(box, "EtherCAT");

        XElement pdo = etherCat.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Pdo" &&
                string.Equals(GetAttributeValue(element, "Name"), request.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetAttributeValue(element, "Index"), request.Index, StringComparison.OrdinalIgnoreCase))
            ?? AddChild(etherCat, "Pdo");

        pdo.SetAttributeValue("Name", request.Name);
        pdo.SetAttributeValue("Index", request.Index);
        SetOptionalStringAttribute(pdo, "InOut", request.InOut);
        SetOptionalStringAttribute(pdo, "Flags", request.Flags);
        SetOptionalIntAttribute(pdo, "SyncMan", request.SyncMan);

        if (request.ReplaceExistingEntries)
        {
            foreach (XElement entry in pdo.Elements().Where(element => element.Name.LocalName == "Entry").ToList())
            {
                entry.Remove();
            }
        }

        foreach (IoPdoEntry entryRequest in request.Entries ?? Array.Empty<IoPdoEntry>())
        {
            ApplyIoPdoEntry(pdo, entryRequest, request.ReplaceExistingEntries);
        }

        ApplyIoRawFragments(pdo, request.ExtraFragments, $"Project/Io/Device[@Id='{request.DeviceId}']/Box[@Id='{request.BoxId}']/EtherCAT/Pdo[@Name='{request.Name}']");
    }

    private static void EnsureIoBoxImageInDocument(XDocument document, EnsureIoBoxImageRequest request)
    {
        ValidatePositive(request.DeviceId, nameof(request.DeviceId));
        ValidatePositive(request.BoxId, nameof(request.BoxId));
        ValidatePositive(request.ImageId, nameof(request.ImageId));
        XElement device = FindIoDevice(document, request.DeviceId);
        XElement box = FindIoBoxUnderDevice(device, request.BoxId);
        SetOrCreateChildElementValue(box, "ImageId", request.ImageId.ToString(CultureInfo.InvariantCulture));
        ApplyXmlChildValues(box, request.MetadataValues);
        ApplyIoRawFragments(box, request.MetadataFragments, $"Project/Io/Device[@Id='{request.DeviceId}']/Box[@Id='{request.BoxId}']");
    }

    private static void EnsureMappingInfoInDocument(XDocument document, EnsureMappingInfoRequest request)
    {
        ValidateRequiredText(request.Identifier, nameof(request.Identifier));
        ValidateObjectIdText(request.Id, nameof(request.Id));
        XElement mappings = FindOrCreateRootMappings(document);
        XElement mappingInfo = mappings.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "MappingInfo" &&
                (string.Equals(GetAttributeValue(element, "Identifier"), request.Identifier, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(GetAttributeValue(element, "Id"), request.Id, StringComparison.OrdinalIgnoreCase)))
            ?? AddChild(mappings, "MappingInfo");

        mappingInfo.SetAttributeValue("Identifier", request.Identifier);
        mappingInfo.SetAttributeValue("Id", request.Id);
        ApplyXmlAttributes(mappingInfo, request.Attributes, replaceExisting: true, ignoredNames: ["Identifier", "Id"]);
    }

    private static void EnsureIoMappingLinkInDocument(XDocument document, EnsureIoMappingLinkRequest request)
    {
        ValidateIoMappingLinkRequest(request);
        XElement mappings = FindOrCreateRootMappings(document);
        XElement ownerA = FindOrCreateMappingOwner(mappings, "OwnerA", request.OwnerAName, request.OwnerAPrefix, request.OwnerAType);
        XElement ownerB = FindOrCreateMappingOwner(ownerA, "OwnerB", request.OwnerBName, request.OwnerBPrefix, request.OwnerBType);

        XElement link = ownerB.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Link" &&
                string.Equals(GetAttributeValue(element, "VarA"), request.VarA, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetAttributeValue(element, "VarB"), request.VarB, StringComparison.OrdinalIgnoreCase))
            ?? AddChild(ownerB, "Link");

        link.SetAttributeValue("VarA", request.VarA);
        link.SetAttributeValue("VarB", request.VarB);
        ApplyXmlAttributes(
            link,
            request.LinkAttributes,
            request.ReplaceExistingAttributes,
            ignoredNames: ["VarA", "VarB"]);
    }

    private static XElement FindOrCreateProjectIo(XDocument document)
    {
        XElement project = FindTopLevelProject(document);
        return project.Elements().FirstOrDefault(element => element.Name.LocalName == "Io")
            ?? AddChild(project, "Io");
    }

    private static XElement FindOrCreateRootMappings(XDocument document)
    {
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        return root.Elements().FirstOrDefault(element => element.Name.LocalName == "Mappings")
            ?? AddChild(root, "Mappings");
    }

    private static XElement FindIoDevice(XDocument document, int deviceId)
    {
        ValidatePositive(deviceId, nameof(deviceId));
        XElement io = FindOrCreateProjectIo(document);
        string deviceIdText = deviceId.ToString(CultureInfo.InvariantCulture);
        return io.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Device" &&
                string.Equals(GetAttributeValue(element, "Id"), deviceIdText, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"IO Device Id='{deviceIdText}' was not found in Project/Io.");
    }

    private static XElement FindIoBoxUnderDevice(XElement device, int boxId)
    {
        ValidatePositive(boxId, nameof(boxId));
        string boxIdText = boxId.ToString(CultureInfo.InvariantCulture);
        List<XElement> matches = device.Descendants().Where(element =>
                element.Name.LocalName == "Box" &&
                string.Equals(GetAttributeValue(element, "Id"), boxIdText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"IO Box Id='{boxIdText}' was not found under Device Id='{GetAttributeValue(device, "Id") ?? "<unknown>"}'."),
            _ => throw new InvalidOperationException($"IO Box Id='{boxIdText}' is ambiguous under Device Id='{GetAttributeValue(device, "Id") ?? "<unknown>"}'.")
        };
    }

    private static void ApplyIoAddressInfo(XElement device, IoAddressInfo addressInfo)
    {
        if (!string.IsNullOrWhiteSpace(addressInfo.RawXml))
        {
            ValidateDocumentedRawXml(
                addressInfo.RawXml,
                addressInfo.FragmentSource,
                addressInfo.TargetParentPath,
                addressInfo.FieldMeaning,
                addressInfo.VerificationEvidence,
                "AddressInfo",
                "Project/Io/Device/AddressInfo");
            XElement parsed = ParseAndNormalizeSectionFragment(addressInfo.RawXml, device.GetDefaultNamespace(), "AddressInfo");
            foreach (XElement existing in device.Elements().Where(element => element.Name.LocalName == "AddressInfo").ToList())
            {
                existing.Remove();
            }

            device.Add(parsed);
        }

        if (addressInfo.TcComObjectId is null &&
            addressInfo.PnpDeviceDesc is null &&
            addressInfo.PnpDeviceName is null &&
            addressInfo.PnpDeviceData is null)
        {
            return;
        }

        XElement address = GetOrCreateChildElement(device, "AddressInfo");
        if (addressInfo.TcComObjectId is not null)
        {
            ValidateObjectIdText(addressInfo.TcComObjectId, nameof(addressInfo.TcComObjectId));
            XElement tcCom = GetOrCreateChildElement(address, "TcCom");
            SetOrCreateChildElementValue(tcCom, "ObjectId", addressInfo.TcComObjectId);
        }

        if (addressInfo.PnpDeviceDesc is not null ||
            addressInfo.PnpDeviceName is not null ||
            addressInfo.PnpDeviceData is not null)
        {
            XElement pnp = GetOrCreateChildElement(address, "Pnp");
            SetOptionalChildValue(pnp, "DeviceDesc", addressInfo.PnpDeviceDesc);
            SetOptionalChildValue(pnp, "DeviceName", addressInfo.PnpDeviceName);
            SetOptionalChildValue(pnp, "DeviceData", addressInfo.PnpDeviceData);
        }
    }

    private static void ApplyIoImageDefinition(XElement device, IoImageDefinition image)
    {
        ValidatePositive(image.Id, nameof(image.Id));
        ValidatePositive(image.AddrType, nameof(image.AddrType));
        ValidatePositive(image.ImageType, nameof(image.ImageType));
        ValidateRequiredText(image.Name, nameof(image.Name));

        string imageIdText = image.Id.ToString(CultureInfo.InvariantCulture);
        XElement imageElement = device.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Image" &&
                string.Equals(GetAttributeValue(element, "Id"), imageIdText, StringComparison.OrdinalIgnoreCase))
            ?? AddChild(device, "Image");

        imageElement.SetAttributeValue("Id", imageIdText);
        imageElement.SetAttributeValue("AddrType", image.AddrType.ToString(CultureInfo.InvariantCulture));
        imageElement.SetAttributeValue("ImageType", image.ImageType.ToString(CultureInfo.InvariantCulture));
        SetOptionalIntAttribute(imageElement, "SizeIn", image.SizeIn);
        SetOptionalIntAttribute(imageElement, "SizeOut", image.SizeOut);
        SetOrCreateChildElementValue(imageElement, "Name", image.Name);
    }

    private static void ApplyIoPdoEntry(XElement pdo, IoPdoEntry entryRequest, bool entriesWereReplaced)
    {
        if (entryRequest.Name is null &&
            entryRequest.Index is null &&
            entryRequest.Sub is null &&
            entryRequest.Type is null &&
            (entryRequest.Attributes?.Count ?? 0) == 0 &&
            (entryRequest.ChildValues?.Count ?? 0) == 0)
        {
            throw new InvalidOperationException("IO PDO entry must contain at least one name, index, subindex, type, attribute, or child value.");
        }

        XElement entry;
        if (entriesWereReplaced)
        {
            entry = AddChild(pdo, "Entry");
        }
        else
        {
            entry = pdo.Elements().FirstOrDefault(element =>
                    element.Name.LocalName == "Entry" &&
                    (entryRequest.Name is null || string.Equals(GetAttributeValue(element, "Name"), entryRequest.Name, StringComparison.OrdinalIgnoreCase)) &&
                    (entryRequest.Index is null || string.Equals(GetAttributeValue(element, "Index"), entryRequest.Index, StringComparison.OrdinalIgnoreCase)) &&
                    (entryRequest.Sub is null || string.Equals(GetAttributeValue(element, "Sub"), entryRequest.Sub, StringComparison.OrdinalIgnoreCase)))
                ?? AddChild(pdo, "Entry");
        }

        SetOptionalStringAttribute(entry, "Name", entryRequest.Name);
        SetOptionalStringAttribute(entry, "Index", entryRequest.Index);
        SetOptionalStringAttribute(entry, "Sub", entryRequest.Sub);
        ApplyXmlAttributes(entry, entryRequest.Attributes, replaceExisting: true, ignoredNames: ["Name", "Index", "Sub"]);
        SetOptionalChildValue(entry, "Type", entryRequest.Type);
        ApplyXmlChildValues(entry, entryRequest.ChildValues, ignoredNames: ["Type"]);
    }

    private static XElement FindOrCreateMappingOwner(XElement parent, string localName, string name, string? prefix, string? type)
    {
        IEnumerable<XElement> matches = parent.Elements().Where(element =>
            element.Name.LocalName == localName &&
            string.Equals(GetAttributeValue(element, "Name"), name, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            matches = matches.Where(element => string.Equals(GetAttributeValue(element, "Prefix"), prefix, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            matches = matches.Where(element => string.Equals(GetAttributeValue(element, "Type"), type, StringComparison.OrdinalIgnoreCase));
        }

        XElement? owner = matches.FirstOrDefault();
        owner ??= AddOwnerElement(parent, localName, name);
        SetOptionalStringAttribute(owner, "Prefix", prefix);
        SetOptionalStringAttribute(owner, "Type", type);
        return owner;
    }

    private static void ApplyIoRawFragments(XElement parent, IReadOnlyList<IoRawXmlFragment>? fragments, string defaultTargetParentPath)
    {
        foreach (IoRawXmlFragment fragment in fragments ?? Array.Empty<IoRawXmlFragment>())
        {
            ValidateDocumentedRawXml(
                fragment.FragmentXml,
                fragment.FragmentSource,
                fragment.TargetParentPath,
                fragment.FieldMeaning,
                fragment.VerificationEvidence,
                expectedElementName: null,
                defaultTargetParentPath);

            XElement parsed = CloneWithNamespace(XElement.Parse(fragment.FragmentXml), parent.GetDefaultNamespace());
            string matchElementName = string.IsNullOrWhiteSpace(fragment.MatchElementName)
                ? parsed.Name.LocalName
                : fragment.MatchElementName!;
            string? matchNameValue = string.IsNullOrWhiteSpace(fragment.MatchNameValue)
                ? GetChildElementValue(parsed, "Name") ?? GetAttributeValue(parsed, "Name")
                : fragment.MatchNameValue;
            XElement? existing = parent.Elements().FirstOrDefault(element =>
                element.Name.LocalName == matchElementName &&
                (matchNameValue is null ||
                 string.Equals(GetChildElementValue(element, "Name") ?? GetAttributeValue(element, "Name"), matchNameValue, StringComparison.OrdinalIgnoreCase)));

            if (existing is null)
            {
                parent.Add(parsed);
            }
            else if (fragment.ReplaceExisting)
            {
                existing.ReplaceWith(parsed);
            }
        }
    }

    private static void ApplyXmlAttributes(
        XElement target,
        IReadOnlyList<TsprojXmlAttribute>? attributes,
        bool replaceExisting,
        IReadOnlyList<string>? ignoredNames = null)
    {
        HashSet<string> ignored = ignoredNames is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : ignoredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (TsprojXmlAttribute attribute in attributes ?? Array.Empty<TsprojXmlAttribute>())
        {
            ValidateRequiredText(attribute.Name, nameof(attribute.Name));
            if (ignored.Contains(attribute.Name))
            {
                continue;
            }

            XAttribute? existing = target.Attributes().FirstOrDefault(item => item.Name.LocalName == attribute.Name);
            if (existing is null || replaceExisting)
            {
                target.SetAttributeValue(attribute.Name, attribute.Value);
            }
        }
    }

    private static void ApplyXmlChildValues(
        XElement target,
        IReadOnlyList<TsprojXmlChildValue>? values,
        IReadOnlyList<string>? ignoredNames = null)
    {
        HashSet<string> ignored = ignoredNames is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : ignoredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (TsprojXmlChildValue value in values ?? Array.Empty<TsprojXmlChildValue>())
        {
            ValidateRequiredText(value.ElementName, nameof(value.ElementName));
            if (ignored.Contains(value.ElementName))
            {
                continue;
            }

            SetOrCreateChildElementValue(target, value.ElementName, value.Value);
        }
    }

    private static void SetOptionalStringAttribute(XElement element, string attributeName, string? value)
    {
        if (value is null)
        {
            return;
        }

        element.SetAttributeValue(attributeName, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    private static void SetOptionalIntAttribute(XElement element, string attributeName, int? value)
    {
        if (value.HasValue)
        {
            element.SetAttributeValue(attributeName, value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void SetOptionalBoolAttribute(XElement element, string attributeName, bool? value, bool removeWhenFalse)
    {
        if (!value.HasValue)
        {
            return;
        }

        element.SetAttributeValue(attributeName, !value.Value && removeWhenFalse ? null : value.Value ? "true" : "false");
    }

    private static void SetOptionalChildValue(XElement parent, string localName, string? value)
    {
        if (value is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            parent.Elements().Where(element => element.Name.LocalName == localName).Remove();
            return;
        }

        SetOrCreateChildElementValue(parent, localName, value);
    }

    private static void ValidateIoDeviceRequest(EnsureIoDeviceRequest request)
    {
        ValidatePositive(request.DeviceId, nameof(request.DeviceId));
        ValidateRequiredText(request.Name, nameof(request.Name));
        ValidatePositive(request.DevType, nameof(request.DevType));
    }

    private static void ValidateEthercatBoxRequest(EnsureEthercatBoxRequest request)
    {
        ValidatePositive(request.DeviceId, nameof(request.DeviceId));
        ValidatePositive(request.BoxId, nameof(request.BoxId));
        ValidateRequiredText(request.Name, nameof(request.Name));
        ValidatePositive(request.BoxType, nameof(request.BoxType));
        if (request.ParentBoxId.HasValue && request.ParentBoxId.Value <= 0)
        {
            throw new InvalidOperationException("ParentBoxId must be greater than zero when provided.");
        }
    }

    private static void ValidateIoPdoRequest(EnsureIoPdoRequest request)
    {
        ValidatePositive(request.DeviceId, nameof(request.DeviceId));
        ValidatePositive(request.BoxId, nameof(request.BoxId));
        ValidateRequiredText(request.Name, nameof(request.Name));
        ValidateRequiredText(request.Index, nameof(request.Index));
    }

    private static void ValidateIoMappingLinkRequest(EnsureIoMappingLinkRequest request)
    {
        ValidateRequiredText(request.OwnerAName, nameof(request.OwnerAName));
        ValidateRequiredText(request.OwnerBName, nameof(request.OwnerBName));
        ValidateRequiredText(request.VarA, nameof(request.VarA));
        ValidateRequiredText(request.VarB, nameof(request.VarB));
    }

    private static void ValidatePositive(int value, string fieldName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"{fieldName} must be greater than zero.");
        }
    }

    private static void ValidateDocumentedRawXml(
        string? fragmentXml,
        string? fragmentSource,
        string? targetParentPath,
        string? fieldMeaning,
        string? verificationEvidence,
        string? expectedElementName,
        string defaultTargetParentPath)
    {
        ValidateRequiredText(fragmentXml, nameof(fragmentXml));
        if (string.IsNullOrWhiteSpace(fragmentSource) ||
            string.IsNullOrWhiteSpace(fieldMeaning) ||
            string.IsNullOrWhiteSpace(verificationEvidence))
        {
            throw new InvalidOperationException(
                "IO raw XML fragments require FragmentSource, FieldMeaning, and VerificationEvidence. Use structured IO fields when the XML field meaning is not documented.");
        }

        if (string.IsNullOrWhiteSpace(targetParentPath) && string.IsNullOrWhiteSpace(defaultTargetParentPath))
        {
            throw new InvalidOperationException("IO raw XML fragments require TargetParentPath or a dedicated API supplied parent path.");
        }

        if (!string.IsNullOrWhiteSpace(expectedElementName))
        {
            XElement parsed = XElement.Parse(fragmentXml!);
            if (!string.Equals(parsed.Name.LocalName, expectedElementName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Expected an IO '{expectedElementName}' fragment but received '{parsed.Name.LocalName}'.");
            }
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
        XElement system = FindOrCreateProjectSystem(document);
        XElement tasks = system.Elements().FirstOrDefault(element => element.Name.LocalName == "Tasks")
            ?? AddChild(system, "Tasks");
        return tasks;
    }

    private static XElement FindOrCreateProjectSystem(XDocument document)
    {
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        XElement project = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Project")
            ?? AddChild(root, "Project");
        return project.Elements().FirstOrDefault(element => element.Name.LocalName == "System")
            ?? AddChild(project, "System");
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
        XElement target = value;
        if (request.ArrayIndex.HasValue)
        {
            target.Elements()
                .Where(element => element.Name.LocalName is "OTCID" or "AreaNo" or "ByteOffs" or "ByteSize")
                .Remove();
            target = FindDataPointerArrayEntry(value, request.ArrayIndex.Value) ?? AddChild(value, "Data");
            target.SetAttributeValue("ArrayIndex", request.ArrayIndex.Value.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            target.Elements()
                .Where(element => element.Name.LocalName == "Data")
                .Remove();
        }

        SetOrCreateChildElementValue(target, "OTCID", request.ObjectId);
        SetOrCreateChildElementValue(target, "AreaNo", request.AreaNo.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(target, "ByteOffs", request.ByteOffset.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(target, "ByteSize", request.ByteSize.ToString(CultureInfo.InvariantCulture));
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

        if (request.ArrayIndex < 0)
        {
            throw new InvalidOperationException("Data pointer ArrayIndex must be greater than or equal to 0.");
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

    private static Dictionary<string, XElement> ReadTmcModulesByName(XDocument tmcDocument)
    {
        XElement? modules = tmcDocument.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "Modules");
        if (modules is null)
        {
            return new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, XElement> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (XElement module in modules.Elements().Where(element => element.Name.LocalName == "Module"))
        {
            string? name = GetChildElementValue(module, "Name") ?? GetAttributeValue(module, "ClassName");
            if (!string.IsNullOrWhiteSpace(name))
            {
                result[name] = module;
            }
        }

        return result;
    }

    private static XElement CreateTmcDescFromModule(
        XElement module,
        XElement? oldTmcDesc,
        CppInstanceTmcDescRefreshItem item,
        bool preserveValueSections,
        bool preserveContextValues)
    {
        string moduleGuid = GetAttributeValue(module, "GUID")
            ?? GetChildElementValue(module, "CLSID")
            ?? throw new InvalidOperationException($"Module '{item.ModuleClassName}' does not expose a GUID.");
        string classFactory = GetAttributeValue(module.Elements().FirstOrDefault(element => element.Name.LocalName == "CLSID") ?? module, "ClassFactory")
            ?? "MotionControl";
        string classFactoryId = item.ClassFactoryId
            ?? GetAttributeValue(oldTmcDesc ?? module, "ClassFactoryId")
            ?? $"C++ Module Vendor|{classFactory}|0.0.0.1";

        XElement desc = new("TmcDesc");
        desc.SetAttributeValue("GUID", NormalizeGuidAttribute(moduleGuid));
        desc.SetAttributeValue("ClassFactoryId", classFactoryId);

        foreach (XElement child in module.Elements())
        {
            if (child.Name.LocalName is "Deployment" or "Properties")
            {
                continue;
            }

            if (preserveContextValues &&
                child.Name.LocalName == "Contexts" &&
                oldTmcDesc?.Elements().FirstOrDefault(element => element.Name.LocalName == "Contexts") is XElement oldContexts)
            {
                desc.Add(CloneWithoutNamespace(oldContexts));
                continue;
            }

            desc.Add(CloneWithoutNamespace(child));
        }

        if (preserveValueSections)
        {
            RemoveTmcDescValueSections(desc);
            AddPreservedOrEmptyValueSection(desc, oldTmcDesc, "ParameterValues");
            AddPreservedOrEmptyValueSection(desc, oldTmcDesc, "InterfacePointerValues");
            AddPreservedOrEmptyValueSection(desc, oldTmcDesc, "DataPointerValues");
        }

        return desc;
    }

    private static void RemoveTmcDescValueSections(XElement tmcDesc)
    {
        foreach (XElement section in tmcDesc.Elements()
                     .Where(element => element.Name.LocalName is "ParameterValues" or "InterfacePointerValues" or "DataPointerValues")
                     .ToList())
        {
            section.Remove();
        }
    }

    private static void AddPreservedOrEmptyValueSection(XElement tmcDesc, XElement? oldTmcDesc, string localName)
    {
        XElement? oldSection = oldTmcDesc?.Elements().FirstOrDefault(element => element.Name.LocalName == localName);
        tmcDesc.Add(oldSection is null ? new XElement(localName) : CloneWithoutNamespace(oldSection));
    }

    private static void ImportDataTypesFromTmc(XDocument document, XDocument tmcDocument)
    {
        XElement? sourceDataTypes = tmcDocument.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "DataTypes");
        if (sourceDataTypes is null || !sourceDataTypes.Elements().Any(element => element.Name.LocalName == "DataType"))
        {
            return;
        }

        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        XElement imported = CloneWithoutNamespace(sourceDataTypes);
        XElement? existing = root.Elements().FirstOrDefault(element => element.Name.LocalName == "DataTypes");
        if (existing is null)
        {
            XElement? project = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Project");
            if (project is null)
            {
                root.Add(imported);
            }
            else
            {
                project.AddBeforeSelf(imported);
            }
        }
        else
        {
            existing.ReplaceWith(imported);
        }
    }

    private static string NormalizeGuidAttribute(string value)
    {
        if (!Guid.TryParse(value, out Guid guid))
        {
            throw new InvalidOperationException($"Invalid GUID text: '{value}'.");
        }

        return guid.ToString("B").ToUpperInvariant();
    }

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

    private static XElement? FindDataPointerArrayEntry(XElement value, int arrayIndex) =>
        value.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Data" &&
            int.TryParse(GetAttributeValue(element, "ArrayIndex"), out int existingIndex) &&
            existingIndex == arrayIndex);

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

    private static XElement CloneWithoutNamespace(XElement source)
    {
        XElement clone = new(source.Name.LocalName);
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
                    clone.Add(CloneWithoutNamespace(childElement));
                    break;
                default:
                    clone.Add(node);
                    break;
            }
        }

        return clone;
    }
}
