import type { ReactNode } from "react";
import { TabsContent, TabsList, TabsRoot, TabsTrigger } from "../ui/tabs";

export interface JuTab {
    id: string;
    label: string;
    content: ReactNode;
}

export function JuTabs({ label, tabs }: { label: string; tabs: JuTab[] }) {
    return (
        <TabsRoot className="ju-tabs" defaultValue={tabs[0]?.id}>
            <TabsList aria-label={label} className="ju-tabs-list">
                {tabs.map((tab) => (
                    <TabsTrigger
                        className="ju-tab-trigger"
                        key={tab.id}
                        value={tab.id}
                    >
                        {tab.label}
                    </TabsTrigger>
                ))}
            </TabsList>
            {tabs.map((tab) => (
                <TabsContent
                    className="ju-tab-content"
                    key={tab.id}
                    value={tab.id}
                >
                    {tab.content}
                </TabsContent>
            ))}
        </TabsRoot>
    );
}
