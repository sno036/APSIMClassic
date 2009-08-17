﻿using System.Collections.Generic;
using System.Xml;
using CSGeneral;
using ApsimFile;
using System.IO;
using System;

public class Types
   {
   private static Types Singleton = null;
   private XmlDocument TypesDoc = new XmlDocument();

   public static Types Instance
      {
      get
         {
         if (Singleton == null)
            Singleton = new Types();
         return Singleton;
         }
      }
   public Types()
      {
      // Constructor.
      TypesDoc.AppendChild(TypesDoc.CreateElement("types"));
      }
   public void Clear()
      {
      // Clear all types from the system.
      TypesDoc.DocumentElement.RemoveAll();
      }
   public void Add(XmlNode TypeNode)
      {
      // The node passed in will look like this:
      // <type name="wheat">
      //    <metadata> ...
      //    <model> ...
      // </type>
      TypesDoc.DocumentElement.AppendChild(TypesDoc.ImportNode(TypeNode, true));
      }
   public string[] TypeNames
      {
      get
         {
         return XmlHelper.ChildNames(TypesDoc.DocumentElement, "type");
         }
      }

   public string MetaData(string TypeName, string MetaDataName)
      {
      // Return metadata for the specified type.
      // MetaDataName could be UIType, Image, LargeIcon etc.
      string MetaDataValue = XmlHelper.Value(TypesDoc.DocumentElement, TypeName + "/MetaData/" + MetaDataName);
      return Configuration.RemoveMacros(MetaDataValue);
      }
   public bool IsVisible(string TypeName)
      {
      // Return true if the specified type is visible in the user interface
      return MetaData(TypeName, "ShowInMainTree") == "Yes";
      }
   public bool IsCrop(string TypeName)
      {
      return MetaData(TypeName, "IsCrop") == "Yes";
      }
   public bool AllowComponentAdd(string ChildTypeName, string ParentTypeName)
      {
      // Return true if an instance of the specified ChildTypeName can 
      // be added to an instance of ParentTypeName
      XmlNode DropsNode = XmlHelper.Find(TypesDoc.DocumentElement, ChildTypeName + "/MetaData/Drops");
      if (DropsNode != null)
         {
         List<string> AllowedDrops = XmlHelper.Values(DropsNode, "Drop");
         return CSGeneral.StringManip.IndexOfCaseInsensitive(AllowedDrops, ParentTypeName) != -1;
         }
      return false;
      }
   public List<XmlNode> Variables(string TypeName)
      {
      List<XmlNode> VariableNodes = new List<XmlNode>();
      XmlNode TypeNode = XmlHelper.Find(TypesDoc.DocumentElement, TypeName);
      foreach (XmlNode InfoNode in XmlHelper.ChildNodes(TypeNode, "info"))
         {
         XmlNode VariableNode = XmlHelper.Find(InfoNode, "variables");
         if (VariableNode != null)
            {
            if (XmlHelper.Name(InfoNode) != "Info")
               XmlHelper.SetName(VariableNode, XmlHelper.Name(InfoNode));
            VariableNodes.Add(VariableNode);
            }
         }
      // Return the variables node to the caller for the specified TypeName
      return VariableNodes;
      }
   public List<XmlNode> Events(string TypeName)
      {
      List<XmlNode> EventNodes = new List<XmlNode>();
      XmlNode TypeNode = XmlHelper.Find(TypesDoc.DocumentElement, TypeName);
      foreach (XmlNode InfoNode in XmlHelper.ChildNodes(TypeNode, "info"))
         {
         XmlNode EventNode = XmlHelper.Find(InfoNode, "events");
         if (EventNode != null)
            {
            if (XmlHelper.Name(InfoNode) != "Info")
               XmlHelper.SetName(EventNode, XmlHelper.Name(InfoNode));
            EventNodes.Add(EventNode);
            }
         }
      // Return the variables node to the caller for the specified TypeName
      return EventNodes;
      }
   public string[] Cultivars(string TypeName)
      {
      // Return a list of cultivar names to caller.
      XmlNode CultivarsNode = XmlHelper.Find(TypesDoc.DocumentElement, TypeName + "/Cultivars");
      if (CultivarsNode != null)
         {
         List<string> Values = XmlHelper.Values(CultivarsNode, "Cultivar");
         string[] ReturnValues = new string[Values.Count];
         Values.CopyTo(ReturnValues);
         return ReturnValues;
         }
      else
         return null;
      }
   public XmlNode ApsimToSim(string TypeName)
      {
      return XmlHelper.Find(TypesDoc.DocumentElement, TypeName + "/MetaData/ApsimToSim");
      }
   public string ModelContents(string TypeName)
      {
      // Find a <Model> node for the specified type name.
      XmlNode ModelNode = XmlHelper.Find(TypesDoc.DocumentElement, TypeName + "/Model");
      if (ModelNode == null)
         return "";
      else
         return ModelNode.InnerXml;
      }
   public string ModelContents(string TypeName, string ModelType)
      {
      // Find a specific <Model name="SoilWat"> node. SoilWat is the ModelType
      XmlNode TypeNode = XmlHelper.Find(TypesDoc.DocumentElement, TypeName);
      if (TypeNode != null)
         {
         foreach (XmlNode ModelNode in XmlHelper.ChildNodes(TypeNode, "Model"))
            {
            if (XmlHelper.Name(ModelNode) == ModelType)
               return ModelNode.InnerXml;
            }
         }
      return "";
      }
   public List<string> SmallIconFileNames()
      {
      List<string> FileNames = new List<string>();
      foreach (string TypeName in XmlHelper.ChildNames(TypesDoc.DocumentElement, "type"))
         {
         string FileName = MetaData(TypeName, "SmallIcon");
         if (FileName != "" && File.Exists(FileName))
            FileNames.Add(FileName);
         }
      return FileNames;
      }
   public List<string> LargeIconFileNames()
      {
      List<string> FileNames = new List<string>();
      foreach (string TypeName in XmlHelper.ChildNames(TypesDoc.DocumentElement, "type"))
         {
         string FileName = MetaData(TypeName, "LargeIcon");
         if (FileName != "" && File.Exists(FileName))
            FileNames.Add(FileName);
         }
      return FileNames;
      }
   public List<string> Dlls(string TypeName)
      {
      XmlNode TypeNode = XmlHelper.Find(TypesDoc.DocumentElement, TypeName);
      return XmlHelper.Values(TypeNode, "MetaData/dll");
      }
   public void SetInfo(string TypeName, string ModuleName, string ProbeInfo)
      {
      XmlNode TypeNode = XmlHelper.Find(TypesDoc.DocumentElement, TypeName);
      XmlNode ProbeInfoNode = null;
      if (ModuleName == "")
         ProbeInfoNode = XmlHelper.Find(TypeNode, "Info");
      else
         {
         string[] ProbeInfoNames = XmlHelper.ChildNames(TypeNode, "Info");
         int ProbeInfoIndex = Array.IndexOf(ProbeInfoNames, ModuleName);
         if (ProbeInfoIndex != -1)
            ProbeInfoNode = XmlHelper.ChildNodes(TypeNode, "Info")[ProbeInfoIndex];
         }
      if (ProbeInfoNode == null)
         {
         ProbeInfoNode = TypeNode.AppendChild(TypesDoc.CreateElement("Info"));
         if (ModuleName != "")
            XmlHelper.SetName(ProbeInfoNode, ModuleName);
         }
      XmlDocument ProbeInfoDoc = new XmlDocument();
      ProbeInfoDoc.LoadXml(ProbeInfo);
      ProbeInfoNode.RemoveAll();
      foreach (XmlNode Child in ProbeInfoDoc.DocumentElement)
         ProbeInfoNode.AppendChild(TypesDoc.ImportNode(Child, true));
      }
   public void Save(string TypeName, TextWriter Out)
      {
      XmlNode TypeNode = XmlHelper.Find(TypesDoc.DocumentElement, TypeName);
      Out.Write(XmlHelper.FormattedXML(TypeNode.OuterXml));
      }
   }
