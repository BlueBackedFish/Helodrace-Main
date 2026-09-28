# Linked research

`Helodrace.ResearchLinkDef` treats two research paths as equivalent. Completing
all projects on either side immediately completes all unfinished projects on
the other side. Links are bidirectional and chained links propagate until the
whole chain is synchronized.

```xml
<Helodrace.ResearchLinkDef>
  <defName>HD_Link_AirConditioning</defName>
  <leftProjects>
    <li>AirConditioning</li>
  </leftProjects>
  <rightProjects>
    <li>HelodHeatPump</li>
  </rightProjects>
</Helodrace.ResearchLinkDef>
```

Either side may contain a group. A side containing several projects counts as
complete only when every member is complete. Completing the opposite side will
complete every member of the group.

```xml
<Helodrace.ResearchLinkDef>
  <defName>HD_Link_Machining</defName>
  <leftProjects>
    <li>Machining</li>
  </leftProjects>
  <rightProjects>
    <li>HelodMetalCuttingMachine</li>
    <li>HelodMetalFormingMachine</li>
  </rightProjects>
</Helodrace.ResearchLinkDef>
```

The synchronization also runs when a new game starts or an existing save is
loaded, so adding a link later applies to already-completed research.
