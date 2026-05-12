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

        if (request.TaskId.HasValue)
        {
            ValidatePositive(request.TaskId.Value, nameof(request.TaskId));
            string taskIdText = request.TaskId.Value.ToString(CultureInfo.InvariantCulture);
            XElement? existingTaskWithId = container.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Task" &&
                !ReferenceEquals(element, task) &&
                string.Equals(GetAttributeValue(element, "Id"), taskIdText, StringComparison.OrdinalIgnoreCase));
            if (existingTaskWithId is not null)
            {
                throw new InvalidOperationException($"Task Id '{taskIdText}' is already used by task '{GetChildElementValue(existingTaskWithId, "Name") ?? "<unnamed>"}'.");
            }

            task.SetAttributeValue("Id", taskIdText);
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

        SetOptionalIntAttribute(settings, "MaxCpus", request.MaxCpus);
        SetOptionalIntAttribute(settings, "NonWinCpus", request.NonWinCpus);

        if (request.ReplaceCpuEntries)
        {
            foreach (XElement cpu in settings.Elements().Where(element => element.Name.LocalName == "Cpu").ToList())
            {
                cpu.Remove();
            }
        }

        foreach (SystemCpuSetting cpuEntry in request.CpuEntries ?? Array.Empty<SystemCpuSetting>())
        {
            ApplySystemCpuSetting(settings, cpuEntry);
        }

        if (request.CpuId.HasValue)
        {
            if (request.CpuId.Value < 0)
            {
                throw new InvalidOperationException("CpuId must be greater than or equal to zero.");
            }

            string cpuIdText = request.CpuId.Value.ToString(CultureInfo.InvariantCulture);
            XElement cpu = settings.Elements().FirstOrDefault(element =>
                    element.Name.LocalName == "Cpu" &&
                    string.Equals(GetAttributeValue(element, "CpuId"), cpuIdText, StringComparison.OrdinalIgnoreCase))
                ?? AddChild(settings, "Cpu");
            cpu.SetAttributeValue("CpuId", cpuIdText);
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

    public AssertDataPointerShapeResult AssertDataPointerShape(string tsprojPath, AssertDataPointerShapeRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ValidateRequiredText(request.InstanceName, nameof(request.InstanceName));
        XDocument document = Load(tsprojPath);
        XElement instance = FindInstance(document, request.InstanceName);
        XElement? dataPointerValues = instance.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "TmcDesc")?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "DataPointerValues");

        IReadOnlyList<DataPointerValueShape> dataPointers = ReadDataPointerShapes(dataPointerValues);
        int dataPointerRecordCount = dataPointers.Sum(pointer => pointer.DataRecordCount);
        IReadOnlyList<MappingLinkShape> mappingLinks = ReadRootMappingLinks(document);
        int dataPointerMappingLinkCount = mappingLinks.Count(IsDataPointerMappingLink);
        List<string> errors = [];

        if (request.ExpectedDataPointerRecordCount.HasValue &&
            dataPointerRecordCount != request.ExpectedDataPointerRecordCount.Value)
        {
            errors.Add($"Instance '{request.InstanceName}' has {dataPointerRecordCount} DataPointerValues record(s), expected {request.ExpectedDataPointerRecordCount.Value}.");
        }

        if (request.ExpectedRootMappingLinkCount.HasValue &&
            mappingLinks.Count != request.ExpectedRootMappingLinkCount.Value)
        {
            errors.Add($"Root Mappings has {mappingLinks.Count} Link record(s), expected {request.ExpectedRootMappingLinkCount.Value}.");
        }

        if (request.ExpectedDataPointerMappingLinkCount.HasValue &&
            dataPointerMappingLinkCount != request.ExpectedDataPointerMappingLinkCount.Value)
        {
            errors.Add($"Root Mappings has {dataPointerMappingLinkCount} data pointer Link record(s), expected {request.ExpectedDataPointerMappingLinkCount.Value}.");
        }

        foreach (ExpectedDataPointerValueShape expected in request.DataPointers ?? Array.Empty<ExpectedDataPointerValueShape>())
        {
            ValidateRequiredText(expected.PointerName, nameof(expected.PointerName));
            DataPointerValueShape? actual = dataPointers.FirstOrDefault(pointer =>
                string.Equals(pointer.PointerName, expected.PointerName, StringComparison.OrdinalIgnoreCase));
            if (actual is null)
            {
                errors.Add($"DataPointerValues entry '{expected.PointerName}' is missing on instance '{request.InstanceName}'.");
                continue;
            }

            if (expected.DataRecordCount.HasValue && actual.DataRecordCount != expected.DataRecordCount.Value)
            {
                errors.Add($"DataPointerValues entry '{expected.PointerName}' has {actual.DataRecordCount} record(s), expected {expected.DataRecordCount.Value}.");
            }

            foreach (int arrayIndex in expected.ArrayIndexes ?? Array.Empty<int>())
            {
                if (!actual.ArrayIndexes.Contains(arrayIndex))
                {
                    errors.Add($"DataPointerValues entry '{expected.PointerName}' is missing Data ArrayIndex='{arrayIndex}'.");
                }
            }
        }

        foreach (ExpectedMappingLinkShape expected in request.MappingLinks ?? Array.Empty<ExpectedMappingLinkShape>())
        {
            ValidateRequiredText(expected.OwnerAName, nameof(expected.OwnerAName));
            ValidateRequiredText(expected.OwnerBName, nameof(expected.OwnerBName));
            ValidateRequiredText(expected.VarA, nameof(expected.VarA));
            ValidateRequiredText(expected.VarB, nameof(expected.VarB));
            bool found = mappingLinks.Any(link =>
                string.Equals(link.OwnerAName, expected.OwnerAName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(link.OwnerBName, expected.OwnerBName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(link.VarA, expected.VarA, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(link.VarB, expected.VarB, StringComparison.OrdinalIgnoreCase));
            if (!found)
            {
                errors.Add($"Mapping link '{expected.OwnerAName}:{expected.VarA} -> {expected.OwnerBName}:{expected.VarB}' is missing.");
            }
        }

        bool succeeded = errors.Count == 0;
        string summary = succeeded
            ? $"Data pointer shape for {request.InstanceName} matched: {dataPointerRecordCount} data record(s), {dataPointerMappingLinkCount} data pointer mapping link(s), {mappingLinks.Count} root mapping link(s)."
            : $"Data pointer shape for {request.InstanceName} failed with {errors.Count} error(s).";

        return new AssertDataPointerShapeResult(
            succeeded,
            request.InstanceName,
            dataPointerRecordCount,
            dataPointerMappingLinkCount,
            mappingLinks.Count,
            dataPointers,
            mappingLinks,
            errors,
            summary);
    }

    public AssertIoTopologyShapeResult AssertIoTopologyShape(string tsprojPath, AssertIoTopologyShapeRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        XDocument document = Load(tsprojPath);
        IReadOnlyList<IoDeviceShape> devices = ReadIoDeviceShapes(document);
        IReadOnlyList<IoBoxShape> boxes = ReadIoBoxShapes(document);
        XElement? io = FindProjectIo(document);
        int imageCount = io?.Descendants().Count(element => element.Name.LocalName == "Image") ?? 0;
        int pdoCount = io?.Descendants().Count(element => element.Name.LocalName == "Pdo") ?? 0;
        int pdoEntryCount = io?.Descendants()
            .Where(element => element.Name.LocalName == "Pdo")
            .Sum(pdo => pdo.Elements().Count(element => element.Name.LocalName == "Entry")) ?? 0;
        int mappingInfoCount = ReadRootMappingInfoCount(document);
        int ownerACount = ReadRootOwnerACount(document);
        IReadOnlyList<MappingLinkShape> mappingLinks = ReadRootMappingLinks(document);
        List<string> errors = [];

        if (request.ExpectedDeviceCount.HasValue && devices.Count != request.ExpectedDeviceCount.Value)
        {
            errors.Add($"Project/Io has {devices.Count} Device record(s), expected {request.ExpectedDeviceCount.Value}.");
        }

        if (request.ExpectedBoxCount.HasValue && boxes.Count != request.ExpectedBoxCount.Value)
        {
            errors.Add($"Project/Io has {boxes.Count} Box record(s), expected {request.ExpectedBoxCount.Value}.");
        }

        if (request.ExpectedImageCount.HasValue && imageCount != request.ExpectedImageCount.Value)
        {
            errors.Add($"Project/Io has {imageCount} Image record(s), expected {request.ExpectedImageCount.Value}.");
        }

        if (request.ExpectedPdoCount.HasValue && pdoCount != request.ExpectedPdoCount.Value)
        {
            errors.Add($"Project/Io has {pdoCount} Pdo record(s), expected {request.ExpectedPdoCount.Value}.");
        }

        if (request.ExpectedPdoEntryCount.HasValue && pdoEntryCount != request.ExpectedPdoEntryCount.Value)
        {
            errors.Add($"Project/Io has {pdoEntryCount} Pdo Entry record(s), expected {request.ExpectedPdoEntryCount.Value}.");
        }

        if (request.ExpectedMappingInfoCount.HasValue && mappingInfoCount != request.ExpectedMappingInfoCount.Value)
        {
            errors.Add($"Root Mappings has {mappingInfoCount} MappingInfo record(s), expected {request.ExpectedMappingInfoCount.Value}.");
        }

        if (request.ExpectedOwnerACount.HasValue && ownerACount != request.ExpectedOwnerACount.Value)
        {
            errors.Add($"Root Mappings has {ownerACount} OwnerA record(s), expected {request.ExpectedOwnerACount.Value}.");
        }

        if (request.ExpectedRootMappingLinkCount.HasValue && mappingLinks.Count != request.ExpectedRootMappingLinkCount.Value)
        {
            errors.Add($"Root Mappings has {mappingLinks.Count} Link record(s), expected {request.ExpectedRootMappingLinkCount.Value}.");
        }

        foreach (ExpectedIoDeviceShape expected in request.Devices ?? Array.Empty<ExpectedIoDeviceShape>())
        {
            IoDeviceShape? actual = devices.FirstOrDefault(device => device.DeviceId == expected.DeviceId);
            if (actual is null)
            {
                errors.Add($"IO Device Id='{expected.DeviceId}' is missing.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(expected.Name) &&
                !string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"IO Device Id='{expected.DeviceId}' name is '{actual.Name}', expected '{expected.Name}'.");
            }

            if (expected.BoxCount.HasValue && actual.BoxCount != expected.BoxCount.Value)
            {
                errors.Add($"IO Device Id='{expected.DeviceId}' has {actual.BoxCount} Box record(s), expected {expected.BoxCount.Value}.");
            }

            if (!string.IsNullOrWhiteSpace(expected.InfoImageId) &&
                !string.Equals(actual.InfoImageId, expected.InfoImageId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"IO Device Id='{expected.DeviceId}' InfoImageId is '{actual.InfoImageId}', expected '{expected.InfoImageId}'.");
            }

            if (expected.ImageCount.HasValue && actual.ImageCount != expected.ImageCount.Value)
            {
                errors.Add($"IO Device Id='{expected.DeviceId}' has {actual.ImageCount} Image record(s), expected {expected.ImageCount.Value}.");
            }

            if (expected.DirectBoxCount.HasValue && actual.DirectBoxCount != expected.DirectBoxCount.Value)
            {
                errors.Add($"IO Device Id='{expected.DeviceId}' has {actual.DirectBoxCount} direct Box record(s), expected {expected.DirectBoxCount.Value}.");
            }

            if (expected.DirectImageCount.HasValue && actual.DirectImageCount != expected.DirectImageCount.Value)
            {
                errors.Add($"IO Device Id='{expected.DeviceId}' has {actual.DirectImageCount} direct Image record(s), expected {expected.DirectImageCount.Value}.");
            }

            AssertExpectedElementCounts(
                expected.DirectChildElementCounts,
                actual.DirectChildElementCounts,
                $"IO Device Id='{expected.DeviceId}' direct child",
                errors);
            AssertExpectedElementCounts(
                expected.EtherCatChildElementCounts,
                actual.EtherCatChildElementCounts,
                $"IO Device Id='{expected.DeviceId}' EtherCAT child",
                errors);
            AssertExpectedElementCounts(
                expected.EthernetChildElementCounts,
                actual.EthernetChildElementCounts,
                $"IO Device Id='{expected.DeviceId}' Ethernet child",
                errors);
        }

        foreach (ExpectedIoBoxShape expected in request.Boxes ?? Array.Empty<ExpectedIoBoxShape>())
        {
            IoBoxShape? actual = boxes.FirstOrDefault(box => box.DeviceId == expected.DeviceId && box.BoxId == expected.BoxId);
            if (actual is null)
            {
                errors.Add($"IO Box Id='{expected.BoxId}' under Device Id='{expected.DeviceId}' is missing.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(expected.Name) &&
                !string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"IO Box Id='{expected.BoxId}' under Device Id='{expected.DeviceId}' name is '{actual.Name}', expected '{expected.Name}'.");
            }

            if (expected.PdoCount.HasValue && actual.PdoCount != expected.PdoCount.Value)
            {
                errors.Add($"IO Box Id='{expected.BoxId}' under Device Id='{expected.DeviceId}' has {actual.PdoCount} Pdo record(s), expected {expected.PdoCount.Value}.");
            }

            if (!string.IsNullOrWhiteSpace(expected.ImageId) &&
                !string.Equals(actual.ImageId, expected.ImageId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"IO Box Id='{expected.BoxId}' under Device Id='{expected.DeviceId}' ImageId is '{actual.ImageId}', expected '{expected.ImageId}'.");
            }

            if (expected.ParentBoxId.HasValue && actual.ParentBoxId != expected.ParentBoxId.Value)
            {
                errors.Add($"IO Box Id='{expected.BoxId}' under Device Id='{expected.DeviceId}' parent Box Id is '{actual.ParentBoxId}', expected '{expected.ParentBoxId.Value}'.");
            }

            if (!string.IsNullOrWhiteSpace(expected.BoxFlags) &&
                !string.Equals(actual.BoxFlags, expected.BoxFlags, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"IO Box Id='{expected.BoxId}' under Device Id='{expected.DeviceId}' BoxFlags is '{actual.BoxFlags}', expected '{expected.BoxFlags}'.");
            }

            if (expected.PdoEntryCount.HasValue && actual.PdoEntryCount != expected.PdoEntryCount.Value)
            {
                errors.Add($"IO Box Id='{expected.BoxId}' under Device Id='{expected.DeviceId}' has {actual.PdoEntryCount} PDO Entry record(s), expected {expected.PdoEntryCount.Value}.");
            }

            if (expected.DirectChildBoxCount.HasValue && actual.DirectChildBoxCount != expected.DirectChildBoxCount.Value)
            {
                errors.Add($"IO Box Id='{expected.BoxId}' under Device Id='{expected.DeviceId}' has {actual.DirectChildBoxCount} direct child Box record(s), expected {expected.DirectChildBoxCount.Value}.");
            }

            if (expected.TotalChildBoxCount.HasValue && actual.TotalChildBoxCount != expected.TotalChildBoxCount.Value)
            {
                errors.Add($"IO Box Id='{expected.BoxId}' under Device Id='{expected.DeviceId}' has {actual.TotalChildBoxCount} total child Box record(s), expected {expected.TotalChildBoxCount.Value}.");
            }

            AssertExpectedElementCounts(
                expected.DirectChildElementCounts,
                actual.DirectChildElementCounts,
                $"IO Box Id='{expected.BoxId}' under Device Id='{expected.DeviceId}' direct child",
                errors);
            AssertExpectedElementCounts(
                expected.EtherCatChildElementCounts,
                actual.EtherCatChildElementCounts,
                $"IO Box Id='{expected.BoxId}' under Device Id='{expected.DeviceId}' EtherCAT child",
                errors);
        }

        foreach (ExpectedMappingLinkShape expected in request.MappingLinks ?? Array.Empty<ExpectedMappingLinkShape>())
        {
            ValidateRequiredText(expected.OwnerAName, nameof(expected.OwnerAName));
            ValidateRequiredText(expected.OwnerBName, nameof(expected.OwnerBName));
            ValidateRequiredText(expected.VarA, nameof(expected.VarA));
            ValidateRequiredText(expected.VarB, nameof(expected.VarB));
            bool found = mappingLinks.Any(link =>
                string.Equals(link.OwnerAName, expected.OwnerAName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(link.OwnerBName, expected.OwnerBName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(link.VarA, expected.VarA, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(link.VarB, expected.VarB, StringComparison.OrdinalIgnoreCase));
            if (!found)
            {
                errors.Add($"Mapping link '{expected.OwnerAName}:{expected.VarA} -> {expected.OwnerBName}:{expected.VarB}' is missing.");
            }
        }

        bool succeeded = errors.Count == 0;
        string summary = succeeded
            ? $"IO topology shape matched: {devices.Count} device(s), {boxes.Count} box(es), {imageCount} image(s), {pdoCount} PDO(s), {pdoEntryCount} PDO entry/entries, {mappingLinks.Count} link(s)."
            : $"IO topology shape failed with {errors.Count} error(s).";

        return new AssertIoTopologyShapeResult(
            succeeded,
            devices.Count,
            boxes.Count,
            imageCount,
            pdoCount,
            pdoEntryCount,
            mappingInfoCount,
            ownerACount,
            mappingLinks.Count,
            devices,
            boxes,
            mappingLinks,
            errors,
            summary);
    }

    public AssertIoImageReferencesResult AssertIoImageReferences(string tsprojPath, AssertIoImageReferencesRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        XDocument document = Load(tsprojPath);
        XElement? io = FindProjectIo(document);
        HashSet<string> rootImageDataIds = ReadRootImageDataIds(document);
        List<IoDeviceImageReferenceShape> devices = [];
        List<IoImageIdReferenceShape> imageReferences = [];
        Dictionary<string, string> backingById = new(StringComparer.OrdinalIgnoreCase);

        foreach (string id in rootImageDataIds)
        {
            backingById.TryAdd(id, "RootImageData");
        }

        if (io is not null)
        {
            foreach (XElement device in io.Elements().Where(element => element.Name.LocalName == "Device"))
            {
                if (!int.TryParse(GetAttributeValue(device, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceId))
                {
                    continue;
                }

                IReadOnlyList<string> directImageIds = device.Elements()
                    .Where(element => element.Name.LocalName == "Image")
                    .Select(element => GetAttributeValue(element, "Id"))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (string id in directImageIds)
                {
                    backingById.TryAdd(id, "DeviceImage");
                }

                string? infoImageId = GetAttributeValue(device, "InfoImageId");
                if (!string.IsNullOrWhiteSpace(infoImageId))
                {
                    backingById.TryAdd(infoImageId!, "DeviceInfoImageId");
                }

                devices.Add(new IoDeviceImageReferenceShape(
                    deviceId,
                    GetChildElementValue(device, "Name") ?? string.Empty,
                    infoImageId,
                    directImageIds.Count,
                    directImageIds));
            }

            foreach (XElement imageIdElement in io.Descendants().Where(element => element.Name.LocalName == "ImageId"))
            {
                string imageId = imageIdElement.Value.Trim();
                if (string.IsNullOrWhiteSpace(imageId))
                {
                    continue;
                }

                XElement? owner = imageIdElement.Parent;
                string ownerKind = owner?.Name.LocalName ?? string.Empty;
                int? deviceId = TryReadNearestDeviceId(owner);
                int? boxId = TryReadNearestBoxId(owner);
                string? ownerName = owner is null ? null : GetChildElementValue(owner, "Name");
                if (string.IsNullOrWhiteSpace(ownerName) && owner is not null)
                {
                    ownerName = GetAttributeValue(owner, "Name") ?? GetAttributeValue(owner, "Type");
                }

                backingById.TryGetValue(imageId, out string? backingKind);
                bool backed = !string.IsNullOrWhiteSpace(backingKind) ||
                    (request.AllowedUnbackedImageIds?.Any(id => string.Equals(id, imageId, StringComparison.OrdinalIgnoreCase)) ?? false);
                imageReferences.Add(new IoImageIdReferenceShape(
                    ownerKind,
                    deviceId,
                    boxId,
                    ownerName,
                    imageId,
                    backingKind,
                    backed));
            }
        }

        List<string> errors = [];
        int deviceImageCount = devices.Sum(device => device.DirectImageCount);
        int deviceWithInfoImageCount = devices.Count(device => !string.IsNullOrWhiteSpace(device.InfoImageId));
        int deviceInfoWithoutImageCount = devices.Count(device => !string.IsNullOrWhiteSpace(device.InfoImageId) && device.DirectImageCount == 0);
        int backedImageReferenceCount = imageReferences.Count(reference => reference.Backed);
        int unbackedImageReferenceCount = imageReferences.Count(reference => !reference.Backed);

        if (request.ExpectedRootImageDataCount.HasValue && rootImageDataIds.Count != request.ExpectedRootImageDataCount.Value)
        {
            errors.Add($"Root ImageDatas has {rootImageDataIds.Count} ImageData record(s), expected {request.ExpectedRootImageDataCount.Value}.");
        }

        if (request.ExpectedDeviceImageCount.HasValue && deviceImageCount != request.ExpectedDeviceImageCount.Value)
        {
            errors.Add($"Project/Io has {deviceImageCount} direct device Image record(s), expected {request.ExpectedDeviceImageCount.Value}.");
        }

        if (request.ExpectedImageReferenceCount.HasValue && imageReferences.Count != request.ExpectedImageReferenceCount.Value)
        {
            errors.Add($"Project/Io has {imageReferences.Count} ImageId reference(s), expected {request.ExpectedImageReferenceCount.Value}.");
        }

        if (request.RequireDeviceImageForInfoImageId)
        {
            foreach (IoDeviceImageReferenceShape device in devices.Where(device => !string.IsNullOrWhiteSpace(device.InfoImageId) && device.DirectImageCount == 0))
            {
                errors.Add($"IO Device Id='{device.DeviceId}' has InfoImageId='{device.InfoImageId}' but no direct Image node.");
            }
        }

        if (request.RequireImageIdBacking)
        {
            foreach (IoImageIdReferenceShape reference in imageReferences.Where(reference => !reference.Backed))
            {
                string owner = string.IsNullOrWhiteSpace(reference.OwnerName) ? reference.OwnerKind : $"{reference.OwnerKind} '{reference.OwnerName}'";
                errors.Add($"ImageId '{reference.ImageId}' on {owner} has no matching root ImageData, device Image, device InfoImageId, or allowed unbacked id.");
            }
        }

        bool succeeded = errors.Count == 0;
        string summary = succeeded
            ? $"IO image references matched: {rootImageDataIds.Count} root image data record(s), {deviceImageCount} device image(s), {imageReferences.Count} ImageId reference(s)."
            : $"IO image references failed with {errors.Count} error(s).";

        return new AssertIoImageReferencesResult(
            succeeded,
            rootImageDataIds.Count,
            deviceImageCount,
            deviceWithInfoImageCount,
            deviceInfoWithoutImageCount,
            imageReferences.Count,
            backedImageReferenceCount,
            unbackedImageReferenceCount,
            rootImageDataIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
            devices.OrderBy(device => device.DeviceId).ToList(),
            imageReferences
                .OrderBy(reference => reference.DeviceId ?? 0)
                .ThenBy(reference => reference.BoxId ?? 0)
                .ThenBy(reference => reference.OwnerKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(reference => reference.ImageId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            errors,
            summary);
    }

    public DescribeIoTopologyResult DescribeIoTopology(string tsprojPath, DescribeIoTopologyRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.MaxItemsPerCollection < 0)
        {
            throw new InvalidOperationException("DescribeIoTopology MaxItemsPerCollection must be greater than or equal to zero.");
        }

        XDocument document = Load(tsprojPath);
        XElement? io = FindProjectIo(document);
        int deviceCount = io?.Elements().Count(element => element.Name.LocalName == "Device") ?? 0;
        int boxCount = io?.Descendants().Count(element => element.Name.LocalName == "Box") ?? 0;
        int pdoCount = io?.Descendants().Count(element => element.Name.LocalName == "Pdo") ?? 0;
        int pdoEntryCount = io?.Descendants()
            .Where(element => element.Name.LocalName == "Pdo")
            .Sum(pdo => pdo.Elements().Count(element => element.Name.LocalName == "Entry")) ?? 0;
        int imageCount = io?.Descendants().Count(element => element.Name.LocalName == "Image") ?? 0;
        int mappingInfoCount = ReadRootMappingInfoCount(document);
        int ownerACount = ReadRootOwnerACount(document);
        IReadOnlyList<MappingLinkShape> allMappingLinks = ReadRootMappingLinks(document);

        bool truncated = false;
        IReadOnlyList<IoTopologyDeviceDescription> devices = request.IncludeDevices
            ? LimitCollection(DescribeIoDevices(document, request.IncludeAttributes), request.MaxItemsPerCollection, ref truncated)
            : Array.Empty<IoTopologyDeviceDescription>();
        IReadOnlyList<IoTopologyBoxDescription> boxes = request.IncludeBoxes
            ? LimitCollection(DescribeIoBoxes(document, request.IncludeAttributes), request.MaxItemsPerCollection, ref truncated)
            : Array.Empty<IoTopologyBoxDescription>();
        IReadOnlyList<IoTopologyImageDescription> images = request.IncludeDevices || request.IncludeBoxes
            ? LimitCollection(DescribeIoImages(document, request.IncludeAttributes), request.MaxItemsPerCollection, ref truncated)
            : Array.Empty<IoTopologyImageDescription>();
        IReadOnlyList<IoTopologyPdoDescription> pdos = request.IncludePdos
            ? LimitCollection(DescribeIoPdos(document, request.IncludeAttributes), request.MaxItemsPerCollection, ref truncated)
            : Array.Empty<IoTopologyPdoDescription>();
        IReadOnlyList<IoTopologyMappingInfoDescription> mappingInfos = request.IncludeMappings
            ? LimitCollection(DescribeMappingInfos(document, request.IncludeAttributes), request.MaxItemsPerCollection, ref truncated)
            : Array.Empty<IoTopologyMappingInfoDescription>();
        IReadOnlyList<IoTopologyOwnerDescription> owners = request.IncludeMappings
            ? LimitCollection(DescribeMappingOwners(document), request.MaxItemsPerCollection, ref truncated)
            : Array.Empty<IoTopologyOwnerDescription>();
        IReadOnlyList<MappingLinkShape> mappingLinks = request.IncludeMappings
            ? LimitCollection(allMappingLinks, request.MaxItemsPerCollection, ref truncated)
            : Array.Empty<MappingLinkShape>();

        string summary = $"IO topology described: {deviceCount} device(s), {boxCount} box(es), {pdoCount} PDO(s), {allMappingLinks.Count} mapping link(s).";
        if (truncated)
        {
            summary += $" Output collections were truncated to {request.MaxItemsPerCollection} item(s).";
        }

        return new DescribeIoTopologyResult(
            true,
            Path.GetFullPath(tsprojPath),
            deviceCount,
            boxCount,
            imageCount,
            pdoCount,
            pdoEntryCount,
            mappingInfoCount,
            ownerACount,
            allMappingLinks.Count,
            truncated,
            devices,
            boxes,
            images,
            pdos,
            mappingInfos,
            owners,
            mappingLinks,
            summary);
    }

    public CompareIoTopologyResult CompareIoTopology(string candidateTsprojPath, CompareIoTopologyRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ValidateRequiredText(request.ReferenceProjectPath, nameof(request.ReferenceProjectPath));
        if (request.MaxDifferences < 0)
        {
            throw new InvalidOperationException("CompareIoTopology MaxDifferences must be greater than or equal to zero.");
        }

        DescribeIoTopologyRequest describeRequest = new(
            IncludeDevices: true,
            IncludeBoxes: true,
            IncludePdos: request.IncludePdos,
            IncludeMappings: request.IncludeMappings,
            IncludeAttributes: request.IncludeAttributes,
            MaxItemsPerCollection: 0);
        DescribeIoTopologyResult reference = DescribeIoTopology(request.ReferenceProjectPath, describeRequest);
        DescribeIoTopologyResult candidate = DescribeIoTopology(candidateTsprojPath, describeRequest);
        List<IoTopologyCountComparison> counts =
        [
            new("DeviceCount", reference.DeviceCount, candidate.DeviceCount, reference.DeviceCount == candidate.DeviceCount),
            new("BoxCount", reference.BoxCount, candidate.BoxCount, reference.BoxCount == candidate.BoxCount),
            new("ImageCount", reference.ImageCount, candidate.ImageCount, reference.ImageCount == candidate.ImageCount),
            new("PdoCount", reference.PdoCount, candidate.PdoCount, reference.PdoCount == candidate.PdoCount),
            new("PdoEntryCount", reference.PdoEntryCount, candidate.PdoEntryCount, reference.PdoEntryCount == candidate.PdoEntryCount),
            new("MappingInfoCount", reference.MappingInfoCount, candidate.MappingInfoCount, reference.MappingInfoCount == candidate.MappingInfoCount),
            new("OwnerACount", reference.OwnerACount, candidate.OwnerACount, reference.OwnerACount == candidate.OwnerACount),
            new("RootMappingLinkCount", reference.RootMappingLinkCount, candidate.RootMappingLinkCount, reference.RootMappingLinkCount == candidate.RootMappingLinkCount)
        ];

        List<IoTopologyDifference> differences = [];
        bool truncated = false;
        AddDeviceDifferences(reference.Devices, candidate.Devices, differences, request.MaxDifferences, ref truncated);
        AddBoxDifferences(reference.Boxes, candidate.Boxes, differences, request.MaxDifferences, ref truncated);
        AddImageDifferences(reference.Images, candidate.Images, differences, request.MaxDifferences, ref truncated);
        if (request.IncludePdos)
        {
            AddPdoDifferences(reference.Pdos, candidate.Pdos, differences, request.MaxDifferences, ref truncated);
        }

        if (request.IncludeMappings)
        {
            AddMappingInfoDifferences(reference.MappingInfos, candidate.MappingInfos, differences, request.MaxDifferences, ref truncated);
            AddOwnerDifferences(reference.Owners, candidate.Owners, differences, request.MaxDifferences, ref truncated);
            AddMappingLinkDifferences(reference.MappingLinks, candidate.MappingLinks, differences, request.MaxDifferences, ref truncated);
        }

        foreach (IoTopologyCountComparison count in counts.Where(item => !item.Matches))
        {
            AddDifference(
                differences,
                new IoTopologyDifference("Count", count.Name, "Value", count.Reference.ToString(CultureInfo.InvariantCulture), count.Candidate.ToString(CultureInfo.InvariantCulture)),
                request.MaxDifferences,
                ref truncated);
        }

        bool succeeded = counts.All(item => item.Matches) && differences.Count == 0 && !truncated;
        string summary = succeeded
            ? "IO topology comparison matched."
            : $"IO topology comparison found {differences.Count} difference(s)" + (truncated ? " before truncation." : ".");

        return new CompareIoTopologyResult(
            succeeded,
            Path.GetFullPath(request.ReferenceProjectPath),
            Path.GetFullPath(candidateTsprojPath),
            counts,
            differences,
            truncated,
            summary);
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

        if ((request.EtherCatAttributes?.Count ?? 0) > 0 || (request.EtherCatElements?.Count ?? 0) > 0)
        {
            XElement etherCat = GetOrCreateChildElement(device, "EtherCAT");
            ApplyXmlAttributes(etherCat, request.EtherCatAttributes, replaceExisting: true);
            ApplyStructuredElements(etherCat, request.EtherCatElements, request.ReplaceEtherCatElements);
        }

        if ((request.EthernetAttributes?.Count ?? 0) > 0 || (request.EthernetElements?.Count ?? 0) > 0)
        {
            XElement ethernet = GetOrCreateChildElement(device, "Ethernet");
            ApplyXmlAttributes(ethernet, request.EthernetAttributes, replaceExisting: true);
            ApplyStructuredElements(ethernet, request.EthernetElements, request.ReplaceEthernetElements);
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

        if ((request.EtherCatElements?.Count ?? 0) > 0)
        {
            XElement etherCat = GetOrCreateChildElement(box, "EtherCAT");
            ApplyStructuredElements(etherCat, request.EtherCatElements, request.ReplaceEtherCatElements);
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

    private static void ApplySystemCpuSetting(XElement settings, SystemCpuSetting cpuEntry)
    {
        if (cpuEntry.CpuId.HasValue && cpuEntry.CpuId.Value < 0)
        {
            throw new InvalidOperationException("System CPU CpuId must be greater than or equal to zero.");
        }

        string? cpuIdText = cpuEntry.CpuId?.ToString(CultureInfo.InvariantCulture);
        XElement cpu = settings.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Cpu" &&
                string.Equals(GetAttributeValue(element, "CpuId"), cpuIdText, StringComparison.OrdinalIgnoreCase))
            ?? AddChild(settings, "Cpu");

        if (cpuEntry.CpuId.HasValue)
        {
            cpu.SetAttributeValue("CpuId", cpuIdText);
        }

        ApplyXmlAttributes(cpu, cpuEntry.Attributes, replaceExisting: true, ignoredNames: ["CpuId"]);
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
        SetOptionalStringAttribute(imageElement, "ImageFlags", image.ImageFlags);
        SetOptionalIntAttribute(imageElement, "SizeIn", image.SizeIn);
        SetOptionalIntAttribute(imageElement, "SizeOut", image.SizeOut);
        SetOrCreateChildElementValue(imageElement, "Name", image.Name);
    }

    private static void ApplyStructuredElements(XElement parent, IReadOnlyList<IoStructuredElement>? elements, bool replaceExisting)
    {
        if (elements is null || elements.Count == 0)
        {
            return;
        }

        if (replaceExisting)
        {
            HashSet<string> names = elements
                .Select(item => item.ElementName)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (XElement existing in parent.Elements().Where(element => names.Contains(element.Name.LocalName)).ToList())
            {
                existing.Remove();
            }
        }

        foreach (IoStructuredElement element in elements)
        {
            parent.Add(CreateStructuredElement(parent.GetDefaultNamespace(), element));
        }
    }

    private static XElement CreateStructuredElement(XNamespace ns, IoStructuredElement request)
    {
        ValidateRequiredText(request.ElementName, nameof(request.ElementName));
        XElement element = new(ns + request.ElementName);
        ApplyXmlAttributes(element, request.Attributes, replaceExisting: true);
        if (request.Value is not null)
        {
            element.Value = request.Value;
        }

        foreach (IoStructuredElement child in request.Children ?? Array.Empty<IoStructuredElement>())
        {
            element.Add(CreateStructuredElement(ns, child));
        }

        return element;
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

    public static string DeriveTaskObjectId(int taskId)
    {
        if (taskId <= 0)
        {
            throw new InvalidOperationException("Task Id must be greater than zero.");
        }

        uint taskObjectId = 0x02010000u | ((uint)taskId << 4);
        return "#x" + taskObjectId.ToString("X8", CultureInfo.InvariantCulture);
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

    private static IReadOnlyList<DataPointerValueShape> ReadDataPointerShapes(XElement? dataPointerValues)
    {
        if (dataPointerValues is null)
        {
            return Array.Empty<DataPointerValueShape>();
        }

        List<DataPointerValueShape> result = [];
        foreach (XElement value in dataPointerValues.Elements().Where(element => element.Name.LocalName == "Value"))
        {
            string? pointerName = GetChildElementValue(value, "Name");
            if (string.IsNullOrWhiteSpace(pointerName))
            {
                continue;
            }

            List<XElement> dataRecords = value.Elements().Where(element => element.Name.LocalName == "Data").ToList();
            int recordCount = dataRecords.Count > 0 ? dataRecords.Count : HasInlineDataPointerRecord(value) ? 1 : 0;
            List<int> arrayIndexes = dataRecords
                .Select(element => GetAttributeValue(element, "ArrayIndex"))
                .Where(text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .Select(text => int.Parse(text!, CultureInfo.InvariantCulture))
                .OrderBy(index => index)
                .ToList();

            result.Add(new DataPointerValueShape(pointerName, recordCount, arrayIndexes));
        }

        return result;
    }

    private static bool HasInlineDataPointerRecord(XElement value) =>
        value.Elements().Any(element => element.Name.LocalName is "OTCID" or "AreaNo" or "ByteOffs" or "ByteSize");

    private static IReadOnlyList<MappingLinkShape> ReadRootMappingLinks(XDocument document)
    {
        XElement? root = document.Root;
        if (root is null)
        {
            return Array.Empty<MappingLinkShape>();
        }

        List<MappingLinkShape> result = [];
        foreach (XElement mappings in root.Elements().Where(element => element.Name.LocalName == "Mappings"))
        {
            foreach (XElement ownerA in mappings.Elements().Where(element => element.Name.LocalName == "OwnerA"))
            {
                string ownerAName = GetAttributeValue(ownerA, "Name") ?? string.Empty;
                foreach (XElement ownerB in ownerA.Elements().Where(element => element.Name.LocalName == "OwnerB"))
                {
                    string ownerBName = GetAttributeValue(ownerB, "Name") ?? string.Empty;
                    foreach (XElement link in ownerB.Elements().Where(element => element.Name.LocalName == "Link"))
                    {
                        string varA = GetAttributeValue(link, "VarA") ?? string.Empty;
                        string varB = GetAttributeValue(link, "VarB") ?? string.Empty;
                        result.Add(new MappingLinkShape(ownerAName, ownerBName, varA, varB));
                    }
                }
            }
        }

        return result;
    }

    private static bool IsDataPointerMappingLink(MappingLinkShape link) =>
        link.VarA.Contains("Data Pointer^", StringComparison.OrdinalIgnoreCase) ||
        link.VarB.Contains("Data Pointer^", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<IoDeviceShape> ReadIoDeviceShapes(XDocument document)
    {
        XElement? io = FindProjectIo(document);
        if (io is null)
        {
            return Array.Empty<IoDeviceShape>();
        }

        List<IoDeviceShape> result = [];
        foreach (XElement device in io.Elements().Where(element => element.Name.LocalName == "Device"))
        {
            if (!int.TryParse(GetAttributeValue(device, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceId))
            {
                continue;
            }

            string name = GetChildElementValue(device, "Name") ?? string.Empty;
            int boxCount = device.Descendants().Count(element => element.Name.LocalName == "Box");
            string? infoImageId = GetAttributeValue(device, "InfoImageId");
            int imageCount = device.Descendants().Count(element => element.Name.LocalName == "Image");
            XElement? etherCat = device.Elements().FirstOrDefault(element => element.Name.LocalName == "EtherCAT");
            XElement? ethernet = device.Elements().FirstOrDefault(element => element.Name.LocalName == "Ethernet");
            result.Add(new IoDeviceShape(
                deviceId,
                name,
                boxCount,
                infoImageId,
                imageCount,
                device.Elements().Count(element => element.Name.LocalName == "Box"),
                device.Elements().Count(element => element.Name.LocalName == "Image"),
                CountDirectChildElements(device),
                CountDirectChildElements(etherCat),
                CountDirectChildElements(ethernet)));
        }

        return result.OrderBy(device => device.DeviceId).ToList();
    }

    private static IReadOnlyList<IoBoxShape> ReadIoBoxShapes(XDocument document)
    {
        XElement? io = FindProjectIo(document);
        if (io is null)
        {
            return Array.Empty<IoBoxShape>();
        }

        List<IoBoxShape> result = [];
        foreach (XElement device in io.Elements().Where(element => element.Name.LocalName == "Device"))
        {
            if (!int.TryParse(GetAttributeValue(device, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceId))
            {
                continue;
            }

            foreach (XElement box in device.Descendants().Where(element => element.Name.LocalName == "Box"))
            {
                if (!int.TryParse(GetAttributeValue(box, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int boxId))
                {
                    continue;
                }

                string name = GetChildElementValue(box, "Name") ?? string.Empty;
                IReadOnlyList<XElement> pdos = ReadDirectBoxPdos(box);
                string? imageId = GetChildElementValue(box, "ImageId");
                XElement? etherCat = box.Elements().FirstOrDefault(element => element.Name.LocalName == "EtherCAT");
                result.Add(new IoBoxShape(
                    deviceId,
                    TryReadNearestBoxId(box.Parent),
                    boxId,
                    name,
                    pdos.Count,
                    imageId,
                    GetAttributeValue(box, "BoxFlags"),
                    pdos.Sum(pdo => pdo.Elements().Count(element => element.Name.LocalName == "Entry")),
                    box.Elements().Count(element => element.Name.LocalName == "Box"),
                    box.Descendants().Count(element => element.Name.LocalName == "Box"),
                    CountDirectChildElements(box),
                    CountDirectChildElements(etherCat)));
            }
        }

        return result.OrderBy(box => box.DeviceId).ThenBy(box => box.BoxId).ToList();
    }

    private static IReadOnlyList<IoTopologyDeviceDescription> DescribeIoDevices(XDocument document, bool includeAttributes)
    {
        XElement? io = FindProjectIo(document);
        if (io is null)
        {
            return Array.Empty<IoTopologyDeviceDescription>();
        }

        List<IoTopologyDeviceDescription> result = [];
        foreach (XElement device in io.Elements().Where(element => element.Name.LocalName == "Device"))
        {
            if (!int.TryParse(GetAttributeValue(device, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceId))
            {
                continue;
            }

            XElement? etherCat = device.Elements().FirstOrDefault(element => element.Name.LocalName == "EtherCAT");
            XElement? ethernet = device.Elements().FirstOrDefault(element => element.Name.LocalName == "Ethernet");
            result.Add(new IoTopologyDeviceDescription(
                deviceId,
                GetChildElementValue(device, "Name") ?? string.Empty,
                GetAttributeValue(device, "DevType"),
                IsTrueAttribute(device, "Disabled"),
                GetAttributeValue(device, "DevFlags"),
                GetAttributeValue(device, "AmsPort"),
                GetAttributeValue(device, "AmsNetId"),
                GetAttributeValue(device, "RemoteName"),
                GetAttributeValue(device, "InfoImageId"),
                device.Elements().Count(element => element.Name.LocalName == "Box"),
                device.Descendants().Count(element => element.Name.LocalName == "Box"),
                device.Elements().Count(element => element.Name.LocalName == "Image"),
                device.Descendants().Count(element => element.Name.LocalName == "Image"),
                CountDirectChildElements(device),
                CountDirectChildElements(etherCat),
                CountDirectChildElements(ethernet),
                includeAttributes ? ReadAttributes(device) : Array.Empty<IoTopologyAttributeDescription>(),
                includeAttributes ? ReadAttributes(etherCat) : Array.Empty<IoTopologyAttributeDescription>(),
                includeAttributes ? ReadAttributes(ethernet) : Array.Empty<IoTopologyAttributeDescription>()));
        }

        return result.OrderBy(device => device.DeviceId).ToList();
    }

    private static IReadOnlyList<IoTopologyBoxDescription> DescribeIoBoxes(XDocument document, bool includeAttributes)
    {
        XElement? io = FindProjectIo(document);
        if (io is null)
        {
            return Array.Empty<IoTopologyBoxDescription>();
        }

        List<IoTopologyBoxDescription> result = [];
        foreach (XElement device in io.Elements().Where(element => element.Name.LocalName == "Device"))
        {
            if (!int.TryParse(GetAttributeValue(device, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceId))
            {
                continue;
            }

            foreach (XElement box in device.Descendants().Where(element => element.Name.LocalName == "Box"))
            {
                if (!int.TryParse(GetAttributeValue(box, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int boxId))
                {
                    continue;
                }

                int? parentBoxId = TryReadNearestBoxId(box.Parent);
                XElement? etherCat = box.Elements().FirstOrDefault(element => element.Name.LocalName == "EtherCAT");
                IReadOnlyList<XElement> pdos = ReadDirectBoxPdos(box);
                result.Add(new IoTopologyBoxDescription(
                    deviceId,
                    parentBoxId,
                    boxId,
                    GetChildElementValue(box, "Name") ?? string.Empty,
                    GetAttributeValue(box, "BoxType"),
                    IsTrueAttribute(box, "Disabled"),
                    GetAttributeValue(box, "BoxFlags"),
                    GetChildElementValue(box, "ImageId"),
                    box.Elements().Count(element => element.Name.LocalName == "Box"),
                    box.Descendants().Count(element => element.Name.LocalName == "Box"),
                    pdos.Count,
                    pdos.Sum(pdo => pdo.Elements().Count(element => element.Name.LocalName == "Entry")),
                    CountDirectChildElements(box),
                    CountDirectChildElements(etherCat),
                    includeAttributes ? ReadAttributes(box) : Array.Empty<IoTopologyAttributeDescription>(),
                    includeAttributes ? ReadAttributes(etherCat) : Array.Empty<IoTopologyAttributeDescription>()));
            }
        }

        return result.OrderBy(box => box.DeviceId).ThenBy(box => box.BoxId).ToList();
    }

    private static IReadOnlyList<IoTopologyImageDescription> DescribeIoImages(XDocument document, bool includeAttributes)
    {
        XElement? io = FindProjectIo(document);
        if (io is null)
        {
            return Array.Empty<IoTopologyImageDescription>();
        }

        List<IoTopologyImageDescription> result = [];
        foreach (XElement device in io.Elements().Where(element => element.Name.LocalName == "Device"))
        {
            if (!int.TryParse(GetAttributeValue(device, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceId))
            {
                continue;
            }

            foreach (XElement image in device.Descendants().Where(element => element.Name.LocalName == "Image"))
            {
                XElement? parent = image.Parent;
                int? parentBoxId = parent?.Name.LocalName == "Box"
                    ? TryReadNearestBoxId(parent)
                    : null;
                result.Add(new IoTopologyImageDescription(
                    deviceId,
                    parent?.Name.LocalName,
                    parentBoxId,
                    GetAttributeValue(image, "Id") ?? string.Empty,
                    GetAttributeValue(image, "AddrType"),
                    GetAttributeValue(image, "ImageType"),
                    GetAttributeValue(image, "ImageFlags"),
                    GetAttributeValue(image, "SizeIn"),
                    GetAttributeValue(image, "SizeOut"),
                    GetChildElementValue(image, "Name") ?? string.Empty,
                    includeAttributes ? ReadAttributes(image) : Array.Empty<IoTopologyAttributeDescription>()));
            }
        }

        return result
            .OrderBy(image => image.DeviceId)
            .ThenBy(image => image.ParentKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(image => image.ParentBoxId ?? 0)
            .ThenBy(image => image.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<IoTopologyPdoDescription> DescribeIoPdos(XDocument document, bool includeAttributes)
    {
        XElement? io = FindProjectIo(document);
        if (io is null)
        {
            return Array.Empty<IoTopologyPdoDescription>();
        }

        List<IoTopologyPdoDescription> result = [];
        foreach (XElement device in io.Elements().Where(element => element.Name.LocalName == "Device"))
        {
            if (!int.TryParse(GetAttributeValue(device, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceId))
            {
                continue;
            }

            foreach (XElement box in device.Descendants().Where(element => element.Name.LocalName == "Box"))
            {
                if (!int.TryParse(GetAttributeValue(box, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int boxId))
                {
                    continue;
                }

                string boxName = GetChildElementValue(box, "Name") ?? string.Empty;
                foreach (XElement pdo in ReadDirectBoxPdos(box))
                {
                    IReadOnlyList<IoTopologyPdoEntryDescription> entries = pdo.Elements()
                        .Where(element => element.Name.LocalName == "Entry")
                        .Select(entry => new IoTopologyPdoEntryDescription(
                            GetAttributeValue(entry, "Name"),
                            GetAttributeValue(entry, "Index"),
                            GetAttributeValue(entry, "Sub"),
                            GetAttributeValue(entry, "Type"),
                            GetAttributeValue(entry, "BitLen") ?? GetChildElementValue(entry, "BitLen"),
                            includeAttributes ? ReadAttributes(entry) : Array.Empty<IoTopologyAttributeDescription>()))
                        .ToList();
                    result.Add(new IoTopologyPdoDescription(
                        deviceId,
                        boxId,
                        boxName,
                        GetAttributeValue(pdo, "Name") ?? string.Empty,
                        GetAttributeValue(pdo, "Index") ?? string.Empty,
                        GetAttributeValue(pdo, "InOut"),
                        GetAttributeValue(pdo, "Flags"),
                        GetAttributeValue(pdo, "SyncMan"),
                        entries.Count,
                        entries,
                        includeAttributes ? ReadAttributes(pdo) : Array.Empty<IoTopologyAttributeDescription>()));
                }
            }
        }

        return result
            .OrderBy(pdo => pdo.DeviceId)
            .ThenBy(pdo => pdo.BoxId)
            .ThenBy(pdo => pdo.Index, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pdo => pdo.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<IoTopologyMappingInfoDescription> DescribeMappingInfos(XDocument document, bool includeAttributes)
    {
        List<IoTopologyMappingInfoDescription> result = [];
        foreach (XElement mappings in ReadRootMappings(document))
        {
            foreach (XElement mappingInfo in mappings.Elements().Where(element => element.Name.LocalName == "MappingInfo"))
            {
                result.Add(new IoTopologyMappingInfoDescription(
                    GetAttributeValue(mappingInfo, "Identifier") ?? string.Empty,
                    GetAttributeValue(mappingInfo, "Id") ?? string.Empty,
                    includeAttributes ? ReadAttributes(mappingInfo) : Array.Empty<IoTopologyAttributeDescription>()));
            }
        }

        return result
            .OrderBy(mappingInfo => mappingInfo.Identifier, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mappingInfo => mappingInfo.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<IoTopologyOwnerDescription> DescribeMappingOwners(XDocument document)
    {
        List<IoTopologyOwnerDescription> result = [];
        foreach (XElement mappings in ReadRootMappings(document))
        {
            foreach (XElement ownerA in mappings.Elements().Where(element => element.Name.LocalName == "OwnerA"))
            {
                result.Add(new IoTopologyOwnerDescription(
                    GetAttributeValue(ownerA, "Name") ?? string.Empty,
                    GetAttributeValue(ownerA, "Prefix"),
                    GetAttributeValue(ownerA, "Type"),
                    ownerA.Elements().Count(element => element.Name.LocalName == "OwnerB"),
                    ownerA.Descendants().Count(element => element.Name.LocalName == "Link")));
            }
        }

        return result.OrderBy(owner => owner.OwnerAName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<XElement> ReadDirectBoxPdos(XElement box)
    {
        XElement? etherCat = box.Elements().FirstOrDefault(element => element.Name.LocalName == "EtherCAT");
        return etherCat is null
            ? Array.Empty<XElement>()
            : etherCat.Elements().Where(element => element.Name.LocalName == "Pdo").ToList();
    }

    private static void AddDeviceDifferences(
        IReadOnlyList<IoTopologyDeviceDescription> reference,
        IReadOnlyList<IoTopologyDeviceDescription> candidate,
        List<IoTopologyDifference> differences,
        int maxDifferences,
        ref bool truncated)
    {
        Dictionary<int, IoTopologyDeviceDescription> candidateById = candidate.ToDictionary(device => device.DeviceId);
        foreach (IoTopologyDeviceDescription referenceDevice in reference)
        {
            string key = referenceDevice.DeviceId.ToString(CultureInfo.InvariantCulture);
            if (!candidateById.TryGetValue(referenceDevice.DeviceId, out IoTopologyDeviceDescription? candidateDevice))
            {
                AddDifference(differences, new IoTopologyDifference("Device", key, "Missing", referenceDevice.Name, null), maxDifferences, ref truncated);
                continue;
            }

            AddStringDifference(differences, "Device", key, "Name", referenceDevice.Name, candidateDevice.Name, maxDifferences, ref truncated);
            AddStringDifference(differences, "Device", key, "DevType", referenceDevice.DevType, candidateDevice.DevType, maxDifferences, ref truncated);
            AddBoolDifference(differences, "Device", key, "Disabled", referenceDevice.Disabled, candidateDevice.Disabled, maxDifferences, ref truncated);
            AddStringDifference(differences, "Device", key, "DevFlags", referenceDevice.DevFlags, candidateDevice.DevFlags, maxDifferences, ref truncated);
            AddIntDifference(differences, "Device", key, "TotalBoxCount", referenceDevice.TotalBoxCount, candidateDevice.TotalBoxCount, maxDifferences, ref truncated);
        }

        foreach (IoTopologyDeviceDescription extra in candidate.Where(device => !reference.Any(referenceDevice => referenceDevice.DeviceId == device.DeviceId)))
        {
            AddDifference(
                differences,
                new IoTopologyDifference("Device", extra.DeviceId.ToString(CultureInfo.InvariantCulture), "Extra", null, extra.Name),
                maxDifferences,
                ref truncated);
        }
    }

    private static void AddBoxDifferences(
        IReadOnlyList<IoTopologyBoxDescription> reference,
        IReadOnlyList<IoTopologyBoxDescription> candidate,
        List<IoTopologyDifference> differences,
        int maxDifferences,
        ref bool truncated)
    {
        Dictionary<string, IoTopologyBoxDescription> candidateByKey = candidate.ToDictionary(BoxKey, StringComparer.OrdinalIgnoreCase);
        HashSet<string> referenceKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (IoTopologyBoxDescription referenceBox in reference)
        {
            string key = BoxKey(referenceBox);
            referenceKeys.Add(key);
            if (!candidateByKey.TryGetValue(key, out IoTopologyBoxDescription? candidateBox))
            {
                AddDifference(differences, new IoTopologyDifference("Box", key, "Missing", referenceBox.Name, null), maxDifferences, ref truncated);
                continue;
            }

            AddStringDifference(differences, "Box", key, "Name", referenceBox.Name, candidateBox.Name, maxDifferences, ref truncated);
            AddStringDifference(differences, "Box", key, "BoxType", referenceBox.BoxType, candidateBox.BoxType, maxDifferences, ref truncated);
            AddBoolDifference(differences, "Box", key, "Disabled", referenceBox.Disabled, candidateBox.Disabled, maxDifferences, ref truncated);
            AddStringDifference(differences, "Box", key, "BoxFlags", referenceBox.BoxFlags, candidateBox.BoxFlags, maxDifferences, ref truncated);
            AddStringDifference(differences, "Box", key, "ImageId", referenceBox.ImageId, candidateBox.ImageId, maxDifferences, ref truncated);
            AddIntDifference(differences, "Box", key, "PdoCount", referenceBox.PdoCount, candidateBox.PdoCount, maxDifferences, ref truncated);
            AddIntDifference(differences, "Box", key, "PdoEntryCount", referenceBox.PdoEntryCount, candidateBox.PdoEntryCount, maxDifferences, ref truncated);
            AddIntDictionaryDifference(differences, "Box", key, "EtherCatChildElementCounts", referenceBox.EtherCatChildElementCounts, candidateBox.EtherCatChildElementCounts, maxDifferences, ref truncated);
        }

        foreach (IoTopologyBoxDescription extra in candidate.Where(box => !referenceKeys.Contains(BoxKey(box))))
        {
            AddDifference(differences, new IoTopologyDifference("Box", BoxKey(extra), "Extra", null, extra.Name), maxDifferences, ref truncated);
        }
    }

    private static void AddImageDifferences(
        IReadOnlyList<IoTopologyImageDescription> reference,
        IReadOnlyList<IoTopologyImageDescription> candidate,
        List<IoTopologyDifference> differences,
        int maxDifferences,
        ref bool truncated)
    {
        Dictionary<string, IoTopologyImageDescription> candidateByKey = candidate.ToDictionary(ImageKey, StringComparer.OrdinalIgnoreCase);
        HashSet<string> referenceKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (IoTopologyImageDescription referenceImage in reference)
        {
            string key = ImageKey(referenceImage);
            referenceKeys.Add(key);
            if (!candidateByKey.TryGetValue(key, out IoTopologyImageDescription? candidateImage))
            {
                AddDifference(differences, new IoTopologyDifference("Image", key, "Missing", referenceImage.Name, null), maxDifferences, ref truncated);
                continue;
            }

            AddStringDifference(differences, "Image", key, "Name", referenceImage.Name, candidateImage.Name, maxDifferences, ref truncated);
            AddStringDifference(differences, "Image", key, "AddrType", referenceImage.AddrType, candidateImage.AddrType, maxDifferences, ref truncated);
            AddStringDifference(differences, "Image", key, "ImageType", referenceImage.ImageType, candidateImage.ImageType, maxDifferences, ref truncated);
            AddStringDifference(differences, "Image", key, "ImageFlags", referenceImage.ImageFlags, candidateImage.ImageFlags, maxDifferences, ref truncated);
            AddStringDifference(differences, "Image", key, "SizeIn", referenceImage.SizeIn, candidateImage.SizeIn, maxDifferences, ref truncated);
            AddStringDifference(differences, "Image", key, "SizeOut", referenceImage.SizeOut, candidateImage.SizeOut, maxDifferences, ref truncated);
        }

        foreach (IoTopologyImageDescription extra in candidate.Where(image => !referenceKeys.Contains(ImageKey(image))))
        {
            AddDifference(differences, new IoTopologyDifference("Image", ImageKey(extra), "Extra", null, extra.Name), maxDifferences, ref truncated);
        }
    }

    private static void AddPdoDifferences(
        IReadOnlyList<IoTopologyPdoDescription> reference,
        IReadOnlyList<IoTopologyPdoDescription> candidate,
        List<IoTopologyDifference> differences,
        int maxDifferences,
        ref bool truncated)
    {
        Dictionary<string, IoTopologyPdoDescription> candidateByKey = candidate.ToDictionary(PdoKey, StringComparer.OrdinalIgnoreCase);
        HashSet<string> referenceKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (IoTopologyPdoDescription referencePdo in reference)
        {
            string key = PdoKey(referencePdo);
            referenceKeys.Add(key);
            if (!candidateByKey.TryGetValue(key, out IoTopologyPdoDescription? candidatePdo))
            {
                AddDifference(differences, new IoTopologyDifference("Pdo", key, "Missing", referencePdo.Name, null), maxDifferences, ref truncated);
                continue;
            }

            AddStringDifference(differences, "Pdo", key, "InOut", referencePdo.InOut, candidatePdo.InOut, maxDifferences, ref truncated);
            AddStringDifference(differences, "Pdo", key, "Flags", referencePdo.Flags, candidatePdo.Flags, maxDifferences, ref truncated);
            AddStringDifference(differences, "Pdo", key, "SyncMan", referencePdo.SyncMan, candidatePdo.SyncMan, maxDifferences, ref truncated);
            AddIntDifference(differences, "Pdo", key, "EntryCount", referencePdo.EntryCount, candidatePdo.EntryCount, maxDifferences, ref truncated);
            AddPdoEntryDifferences(key, referencePdo.Entries, candidatePdo.Entries, differences, maxDifferences, ref truncated);
        }

        foreach (IoTopologyPdoDescription extra in candidate.Where(pdo => !referenceKeys.Contains(PdoKey(pdo))))
        {
            AddDifference(differences, new IoTopologyDifference("Pdo", PdoKey(extra), "Extra", null, extra.Name), maxDifferences, ref truncated);
        }
    }

    private static void AddPdoEntryDifferences(
        string pdoKey,
        IReadOnlyList<IoTopologyPdoEntryDescription> reference,
        IReadOnlyList<IoTopologyPdoEntryDescription> candidate,
        List<IoTopologyDifference> differences,
        int maxDifferences,
        ref bool truncated)
    {
        Dictionary<string, IoTopologyPdoEntryDescription> candidateByKey = candidate.ToDictionary(PdoEntryKey, StringComparer.OrdinalIgnoreCase);
        HashSet<string> referenceKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (IoTopologyPdoEntryDescription referenceEntry in reference)
        {
            string entryKey = PdoEntryKey(referenceEntry);
            string key = pdoKey + "/" + entryKey;
            referenceKeys.Add(entryKey);
            if (!candidateByKey.TryGetValue(entryKey, out IoTopologyPdoEntryDescription? candidateEntry))
            {
                AddDifference(differences, new IoTopologyDifference("PdoEntry", key, "Missing", referenceEntry.Name, null), maxDifferences, ref truncated);
                continue;
            }

            AddStringDifference(differences, "PdoEntry", key, "Name", referenceEntry.Name, candidateEntry.Name, maxDifferences, ref truncated);
            AddStringDifference(differences, "PdoEntry", key, "Index", referenceEntry.Index, candidateEntry.Index, maxDifferences, ref truncated);
            AddStringDifference(differences, "PdoEntry", key, "Sub", referenceEntry.Sub, candidateEntry.Sub, maxDifferences, ref truncated);
            AddStringDifference(differences, "PdoEntry", key, "Type", referenceEntry.Type, candidateEntry.Type, maxDifferences, ref truncated);
            AddStringDifference(differences, "PdoEntry", key, "BitLen", referenceEntry.BitLen, candidateEntry.BitLen, maxDifferences, ref truncated);
        }

        foreach (IoTopologyPdoEntryDescription extra in candidate.Where(entry => !referenceKeys.Contains(PdoEntryKey(entry))))
        {
            AddDifference(
                differences,
                new IoTopologyDifference("PdoEntry", pdoKey + "/" + PdoEntryKey(extra), "Extra", null, extra.Name),
                maxDifferences,
                ref truncated);
        }
    }

    private static void AddMappingInfoDifferences(
        IReadOnlyList<IoTopologyMappingInfoDescription> reference,
        IReadOnlyList<IoTopologyMappingInfoDescription> candidate,
        List<IoTopologyDifference> differences,
        int maxDifferences,
        ref bool truncated)
    {
        HashSet<string> candidateKeys = candidate.Select(MappingInfoKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> referenceKeys = reference.Select(MappingInfoKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (IoTopologyMappingInfoDescription item in reference.Where(item => !candidateKeys.Contains(MappingInfoKey(item))))
        {
            AddDifference(differences, new IoTopologyDifference("MappingInfo", MappingInfoKey(item), "Missing", item.Id, null), maxDifferences, ref truncated);
        }

        foreach (IoTopologyMappingInfoDescription item in candidate.Where(item => !referenceKeys.Contains(MappingInfoKey(item))))
        {
            AddDifference(differences, new IoTopologyDifference("MappingInfo", MappingInfoKey(item), "Extra", null, item.Id), maxDifferences, ref truncated);
        }
    }

    private static void AddOwnerDifferences(
        IReadOnlyList<IoTopologyOwnerDescription> reference,
        IReadOnlyList<IoTopologyOwnerDescription> candidate,
        List<IoTopologyDifference> differences,
        int maxDifferences,
        ref bool truncated)
    {
        Dictionary<string, IoTopologyOwnerDescription> candidateByKey = candidate.ToDictionary(owner => owner.OwnerAName, StringComparer.OrdinalIgnoreCase);
        HashSet<string> referenceKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (IoTopologyOwnerDescription referenceOwner in reference)
        {
            string key = referenceOwner.OwnerAName;
            referenceKeys.Add(key);
            if (!candidateByKey.TryGetValue(key, out IoTopologyOwnerDescription? candidateOwner))
            {
                AddDifference(differences, new IoTopologyDifference("OwnerA", key, "Missing", referenceOwner.LinkCount.ToString(CultureInfo.InvariantCulture), null), maxDifferences, ref truncated);
                continue;
            }

            AddIntDifference(differences, "OwnerA", key, "OwnerBCount", referenceOwner.OwnerBCount, candidateOwner.OwnerBCount, maxDifferences, ref truncated);
            AddIntDifference(differences, "OwnerA", key, "LinkCount", referenceOwner.LinkCount, candidateOwner.LinkCount, maxDifferences, ref truncated);
        }

        foreach (IoTopologyOwnerDescription extra in candidate.Where(owner => !referenceKeys.Contains(owner.OwnerAName)))
        {
            AddDifference(differences, new IoTopologyDifference("OwnerA", extra.OwnerAName, "Extra", null, extra.LinkCount.ToString(CultureInfo.InvariantCulture)), maxDifferences, ref truncated);
        }
    }

    private static void AddMappingLinkDifferences(
        IReadOnlyList<MappingLinkShape> reference,
        IReadOnlyList<MappingLinkShape> candidate,
        List<IoTopologyDifference> differences,
        int maxDifferences,
        ref bool truncated)
    {
        HashSet<string> candidateKeys = candidate.Select(MappingLinkKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> referenceKeys = reference.Select(MappingLinkKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (MappingLinkShape item in reference.Where(item => !candidateKeys.Contains(MappingLinkKey(item))))
        {
            AddDifference(differences, new IoTopologyDifference("MappingLink", MappingLinkKey(item), "Missing", item.VarB, null), maxDifferences, ref truncated);
        }

        foreach (MappingLinkShape item in candidate.Where(item => !referenceKeys.Contains(MappingLinkKey(item))))
        {
            AddDifference(differences, new IoTopologyDifference("MappingLink", MappingLinkKey(item), "Extra", null, item.VarB), maxDifferences, ref truncated);
        }
    }

    private static void AddStringDifference(
        List<IoTopologyDifference> differences,
        string kind,
        string key,
        string field,
        string? reference,
        string? candidate,
        int maxDifferences,
        ref bool truncated)
    {
        if (!string.Equals(reference ?? string.Empty, candidate ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            AddDifference(differences, new IoTopologyDifference(kind, key, field, reference, candidate), maxDifferences, ref truncated);
        }
    }

    private static void AddBoolDifference(
        List<IoTopologyDifference> differences,
        string kind,
        string key,
        string field,
        bool reference,
        bool candidate,
        int maxDifferences,
        ref bool truncated)
    {
        if (reference != candidate)
        {
            AddDifference(differences, new IoTopologyDifference(kind, key, field, reference ? "true" : "false", candidate ? "true" : "false"), maxDifferences, ref truncated);
        }
    }

    private static void AddIntDifference(
        List<IoTopologyDifference> differences,
        string kind,
        string key,
        string field,
        int reference,
        int candidate,
        int maxDifferences,
        ref bool truncated)
    {
        if (reference != candidate)
        {
            AddDifference(differences, new IoTopologyDifference(kind, key, field, reference.ToString(CultureInfo.InvariantCulture), candidate.ToString(CultureInfo.InvariantCulture)), maxDifferences, ref truncated);
        }
    }

    private static void AddIntDictionaryDifference(
        List<IoTopologyDifference> differences,
        string kind,
        string key,
        string field,
        IReadOnlyDictionary<string, int> reference,
        IReadOnlyDictionary<string, int> candidate,
        int maxDifferences,
        ref bool truncated)
    {
        foreach (string name in reference.Keys.Concat(candidate.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            reference.TryGetValue(name, out int referenceValue);
            candidate.TryGetValue(name, out int candidateValue);
            AddIntDifference(differences, kind, key, field + "/" + name, referenceValue, candidateValue, maxDifferences, ref truncated);
        }
    }

    private static void AddDifference(
        List<IoTopologyDifference> differences,
        IoTopologyDifference difference,
        int maxDifferences,
        ref bool truncated)
    {
        if (maxDifferences > 0 && differences.Count >= maxDifferences)
        {
            truncated = true;
            return;
        }

        differences.Add(difference);
    }

    private static string BoxKey(IoTopologyBoxDescription box) =>
        $"{box.DeviceId}/{box.BoxId}";

    private static string ImageKey(IoTopologyImageDescription image) =>
        $"{image.DeviceId}/{image.ParentKind}/{image.ParentBoxId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}/{image.Id}";

    private static string PdoKey(IoTopologyPdoDescription pdo) =>
        $"{pdo.DeviceId}/{pdo.BoxId}/{pdo.Index}/{pdo.Name}";

    private static string PdoEntryKey(IoTopologyPdoEntryDescription entry) =>
        $"{entry.Index}/{entry.Sub}/{entry.Name}";

    private static string MappingInfoKey(IoTopologyMappingInfoDescription mappingInfo) =>
        mappingInfo.Identifier + "|" + mappingInfo.Id;

    private static string MappingLinkKey(MappingLinkShape link) =>
        link.OwnerAName + "|" + link.OwnerBName + "|" + link.VarA + "|" + link.VarB;

    private static IReadOnlyList<T> LimitCollection<T>(IReadOnlyList<T> values, int maxItems, ref bool truncated)
    {
        if (maxItems <= 0 || values.Count <= maxItems)
        {
            return values;
        }

        truncated = true;
        return values.Take(maxItems).ToList();
    }

    private static IReadOnlyDictionary<string, int> CountDirectChildElements(XElement? element)
    {
        if (element is null)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        return element.Elements()
            .GroupBy(child => child.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertExpectedElementCounts(
        IReadOnlyDictionary<string, int>? expected,
        IReadOnlyDictionary<string, int> actual,
        string context,
        List<string> errors)
    {
        if (expected is null || expected.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<string, int> item in expected.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                throw new InvalidOperationException($"{context} element-count key must not be empty.");
            }

            if (item.Value < 0)
            {
                throw new InvalidOperationException($"{context} element-count '{item.Key}' must be greater than or equal to zero.");
            }

            actual.TryGetValue(item.Key, out int actualCount);
            if (actualCount != item.Value)
            {
                errors.Add($"{context} element '{item.Key}' count is {actualCount}, expected {item.Value}.");
            }
        }
    }

    private static IReadOnlyList<IoTopologyAttributeDescription> ReadAttributes(XElement? element)
    {
        if (element is null)
        {
            return Array.Empty<IoTopologyAttributeDescription>();
        }

        return element.Attributes()
            .OrderBy(attribute => attribute.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .Select(attribute => new IoTopologyAttributeDescription(attribute.Name.LocalName, attribute.Value))
            .ToList();
    }

    private static int? TryReadNearestBoxId(XElement? element)
    {
        XElement? current = element;
        while (current is not null)
        {
            if (current.Name.LocalName == "Box" &&
                int.TryParse(GetAttributeValue(current, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                return id;
            }

            current = current.Parent;
        }

        return null;
    }

    private static int? TryReadNearestDeviceId(XElement? element)
    {
        XElement? current = element;
        while (current is not null)
        {
            if (current.Name.LocalName == "Device" &&
                int.TryParse(GetAttributeValue(current, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                return id;
            }

            current = current.Parent;
        }

        return null;
    }

    private static HashSet<string> ReadRootImageDataIds(XDocument document)
    {
        XElement? imageDatas = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "ImageDatas");
        return imageDatas is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : imageDatas.Elements()
                .Where(element => element.Name.LocalName == "ImageData")
                .Select(element => GetAttributeValue(element, "Id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsTrueAttribute(XElement element, string attributeName)
    {
        string? value = GetAttributeValue(element, attributeName);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadRootMappingInfoCount(XDocument document) =>
        ReadRootMappings(document).Sum(mappings => mappings.Elements().Count(element => element.Name.LocalName == "MappingInfo"));

    private static int ReadRootOwnerACount(XDocument document) =>
        ReadRootMappings(document).Sum(mappings => mappings.Elements().Count(element => element.Name.LocalName == "OwnerA"));

    private static IReadOnlyList<XElement> ReadRootMappings(XDocument document)
    {
        XElement? root = document.Root;
        return root is null
            ? Array.Empty<XElement>()
            : root.Elements().Where(element => element.Name.LocalName == "Mappings").ToList();
    }

    private static XElement? FindProjectIo(XDocument document)
    {
        XElement? project = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "Project");
        return project?.Elements().FirstOrDefault(element => element.Name.LocalName == "Io");
    }

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
