public void PropagateElementIDCode()
      {
         UIDocument uidoc = ActiveUIDocument;
         if (uidoc == null)
         {
            TaskDialog.Show("Error", "No active document found. Please open a Revit document first.");
            return;
         }

         Document doc = uidoc.Document;

         // Define parameters to propagate - instance parameters
         var instanceParameters = new List<string>
         {
            "Element ID Code",
            "BIM_Element_Code",
            "Location_ID",
            "Sequence_ID",
            "Asset Type",
            "Loc1",
            "Loc2",
            "Seq no"
         };

         // Define parameters to propagate - type parameters
         var typeParameters = new List<string>
         {
            "Component_ID",
            "Group_ID",
            "Type_ID"
         };

         // Collect all family instances in the document
         var allFamilyInstances = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .ToList();

         int updatedCount = 0;
         int processedCount = 0;
         var parameterUpdateDetails = new Dictionary<string, int>();

         using (Transaction t = new Transaction(doc, "Propagate Parameters to Nested Families"))
         {
            t.Start();

            foreach (FamilyInstance fi in allFamilyInstances)
            {
               // Check if this family instance has any of the instance parameters with values
               bool hasValidParameters = false;
               var instanceParamValues = new Dictionary<string, string>();

               foreach (string paramName in instanceParameters)
               {
                  Parameter param = fi.LookupParameter(paramName);
                  if (param != null && param.HasValue)
                  {
                     string paramValue = param.StorageType == StorageType.String
                        ? param.AsString()
                        : param.AsValueString();

                     if (!string.IsNullOrEmpty(paramValue))
                     {
                        instanceParamValues[paramName] = paramValue;
                        hasValidParameters = true;
                     }
                  }
               }

               // Check if this family instance has any of the type parameters with values
               var typeParamValues = new Dictionary<string, string>();
               ElementId typeId = fi.GetTypeId();
               if (typeId != ElementId.InvalidElementId)
               {
                  Element typeElement = doc.GetElement(typeId);
                  if (typeElement != null)
                  {
                     foreach (string paramName in typeParameters)
                     {
                        Parameter param = typeElement.LookupParameter(paramName);
                        if (param != null && param.HasValue)
                        {
                           string paramValue = param.StorageType == StorageType.String
                              ? param.AsString()
                              : param.AsValueString();

                           if (!string.IsNullOrEmpty(paramValue))
                           {
                              typeParamValues[paramName] = paramValue;
                              hasValidParameters = true;
                           }
                        }
                     }
                  }
               }

               if (!hasValidParameters)
               {
                  continue; // Skip if no valid parameters found
               }

               processedCount++;

               // Process all nested families within this family instance
               Queue<FamilyInstance> queue = new();
               queue.Enqueue(fi);

               while (queue.Count > 0)
               {
                  FamilyInstance current = queue.Dequeue();
                  ICollection<ElementId> subIds = current.GetSubComponentIds();

                  foreach (ElementId id in subIds)
                  {
                     if (doc.GetElement(id) is FamilyInstance subFi)
                     {
                        // Update instance parameters on nested family
                        foreach (var kvp in instanceParamValues)
                        {
                           Parameter nestedParam = subFi.LookupParameter(kvp.Key);
                           if (nestedParam != null)
                           {
                              try
                              {
                                 if (nestedParam.StorageType == StorageType.String)
                                 {
                                    nestedParam.Set(kvp.Value);
                                    updatedCount++;

                                    if (!parameterUpdateDetails.TryGetValue(kvp.Key, out int count))
                                    {
                                       parameterUpdateDetails[kvp.Key] = 1;
                                    }
                                    else
                                    {
                                       parameterUpdateDetails[kvp.Key] = count + 1;
                                    }
                                 }
                              }
                              catch (Exception ex)
                              {
                                 // Log error but continue processing
                                 TaskDialog.Show("Warning",
                                    $"Could not update instance parameter {kvp.Key} for {subFi.Name}: {ex.Message}");
                              }
                           }
                        }

                        // Update type parameters on nested family
                        ElementId nestedTypeId = subFi.GetTypeId();
                        if (nestedTypeId != ElementId.InvalidElementId)
                        {
                           Element nestedTypeElement = doc.GetElement(nestedTypeId);
                           if (nestedTypeElement != null)
                           {
                              foreach (var kvp in typeParamValues)
                              {
                                 Parameter nestedTypeParam = nestedTypeElement.LookupParameter(kvp.Key);
                                 if (nestedTypeParam != null)
                                 {
                                    try
                                    {
                                       if (nestedTypeParam.StorageType == StorageType.String)
                                       {
                                          nestedTypeParam.Set(kvp.Value);
                                          updatedCount++;

                                          if (!parameterUpdateDetails.TryGetValue(kvp.Key, out int count))
                                          {
                                             parameterUpdateDetails[kvp.Key] = 1;
                                          }
                                          else
                                          {
                                             parameterUpdateDetails[kvp.Key] = count + 1;
                                          }
                                       }
                                    }
                                    catch (Exception ex)
                                    {
                                       // Log error but continue processing
                                       TaskDialog.Show("Warning",
                                          $"Could not update type parameter {kvp.Key} for {subFi.Name}: {ex.Message}");
                                    }
                                 }
                              }
                           }
                        }

                        queue.Enqueue(subFi);
                     }
                  }
               }
            }

            t.Commit();
         }

         // Build and show the result message
         string message = $"Host families processed: {processedCount}\n";
         message += $"Total parameter updates: {updatedCount}\n\n";

         if (parameterUpdateDetails.Count > 0)
         {
            message += "Updates by parameter:\n";
            foreach (var kvp in parameterUpdateDetails.OrderByDescending(x => x.Value))
            {
               message += $"- {kvp.Key}: {kvp.Value} update(s)\n";
            }
         }

         if (updatedCount > 0)
         {
            message += "\nSuccessfully propagated parameter values from host families to nested families.";
         }
         else if (processedCount > 0)
         {
            message += "\nFound families with parameters but no nested families to update.";
         }
         else
         {
            message += "\nNo families with target parameters found.";
         }

         TaskDialog.Show("Parameter Propagation Results", message);
      }
