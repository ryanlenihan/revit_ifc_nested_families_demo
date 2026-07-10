# Load the Python Standard and DesignScript Libraries
import sys
import clr
clr.AddReference('ProtoGeometry')
from Autodesk.DesignScript.Geometry import *

# Add Revit references
clr.AddReference('RevitAPI')
clr.AddReference('RevitServices')
from RevitServices.Persistence import DocumentManager
from Autodesk.Revit.DB import *

# The inputs to this node will be stored as a list in the IN variables.
# (You can optionally pass custom parameter lists through IN[0] and IN[1] in the future.)
dataEnteringNode = IN

# Get the active Revit document
doc = DocumentManager.Instance.CurrentDBDocument
if doc is None:
    OUT = "Error: No active Revit document."
    sys.exit()

# --- Define parameters to propagate ---
instanceParameters = [
    "Element ID Code",
    "BIM_Element_Code",
    "Location_ID",
    "Sequence_ID",
    "Asset Type",
    "Loc1",
    "Loc2",
    "Seq no"
]

typeParameters = [
    "Component_ID",
    "Group_ID",
    "Type_ID"
]

# Collect all family instances in the document
allFamilyInstances = FilteredElementCollector(doc) \
                     .OfClass(FamilyInstance) \
                     .ToElements()   # Returns IList[Element], items are FamilyInstance

updatedCount = 0
processedCount = 0
parameterUpdateDetails = {}   # param name -> update count
errorMessages = []            # collect warnings instead of showing dialogs

# Start a transaction
t = Transaction(doc, "Propagate Parameters to Nested Families")
t.Start()

try:
    for fi in allFamilyInstances:
        # --- Check host instance for any target instance parameters with values ---
        hasValid = False
        instanceParamValues = {}
        for paramName in instanceParameters:
            param = fi.LookupParameter(paramName)
            if param and param.HasValue:
                val = param.AsString() if param.StorageType == StorageType.String else param.AsValueString()
                if val:   # not empty
                    instanceParamValues[paramName] = val
                    hasValid = True

        # --- Check host type for target type parameters with values ---
        typeParamValues = {}
        typeId = fi.GetTypeId()
        if typeId != ElementId.InvalidElementId:
            typeElem = doc.GetElement(typeId)
            if typeElem:
                for paramName in typeParameters:
                    param = typeElem.LookupParameter(paramName)
                    if param and param.HasValue:
                        val = param.AsString() if param.StorageType == StorageType.String else param.AsValueString()
                        if val:
                            typeParamValues[paramName] = val
                            hasValid = True

        # Skip families that don't carry any of the target parameters
        if not hasValid:
            continue

        processedCount += 1

        # BFS traversal of nested families (using a Python list as a queue)
        queue = [fi]
        while queue:
            current = queue.pop(0)
            subIds = current.GetSubComponentIds()

            for subId in subIds:
                subElem = doc.GetElement(subId)
                if not isinstance(subElem, FamilyInstance):
                    continue
                subFi = subElem   # type: FamilyInstance

                # --- Update instance parameters on nested family ---
                for pName, pValue in instanceParamValues.items():
                    nestedParam = subFi.LookupParameter(pName)
                    if nestedParam and nestedParam.StorageType == StorageType.String:
                        try:
                            nestedParam.Set(pValue)
                            updatedCount += 1
                            parameterUpdateDetails[pName] = parameterUpdateDetails.get(pName, 0) + 1
                        except Exception as ex:
                            errorMessages.append(
                                "Instance param '{}' on '{}': {}".format(pName, subFi.Name, ex.Message)
                            )

                # --- Update type parameters on the nested family's type ---
                nestedTypeId = subFi.GetTypeId()
                if nestedTypeId != ElementId.InvalidElementId:
                    nestedTypeElem = doc.GetElement(nestedTypeId)
                    if nestedTypeElem:
                        for pName, pValue in typeParamValues.items():
                            nestedTypeParam = nestedTypeElem.LookupParameter(pName)
                            if nestedTypeParam and nestedTypeParam.StorageType == StorageType.String:
                                try:
                                    nestedTypeParam.Set(pValue)
                                    updatedCount += 1
                                    parameterUpdateDetails[pName] = parameterUpdateDetails.get(pName, 0) + 1
                                except Exception as ex:
                                    errorMessages.append(
                                        "Type param '{}' on '{}': {}".format(pName, subFi.Name, ex.Message)
                                    )

                # Add nested family to queue to process its own sub‑components
                queue.append(subFi)

    t.Commit()

except Exception as e:
    t.RollBack()
    OUT = "Transaction failed and was rolled back:\n" + str(e)
    sys.exit()

# --- Build the result message (same style as the macro) ---
message = "Host families processed: {}\n".format(processedCount)
message += "Total parameter updates: {}\n\n".format(updatedCount)

if parameterUpdateDetails:
    message += "Updates by parameter:\n"
    for pName in sorted(parameterUpdateDetails, key=parameterUpdateDetails.get, reverse=True):
        message += "- {}: {} update(s)\n".format(pName, parameterUpdateDetails[pName])

if errorMessages:
    message += "\nWarnings:\n"
    message += "\n".join(errorMessages[:10])   # show at most 10 warnings
    if len(errorMessages) > 10:
        message += "\n... and {} more.".format(len(errorMessages) - 10)

if updatedCount > 0:
    message += "\nSuccessfully propagated parameter values from host families to nested families."
elif processedCount > 0:
    message += "\nFound families with parameters but no nested families to update."
else:
    message += "\nNo families with target parameters found."

OUT = message
