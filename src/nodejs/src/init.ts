import { NextInteraction } from 'next-interaction';
import SwiftClick from 'swiftclick';

const swiftClick = SwiftClick.attach(document.body);
swiftClick.useCssParser(true); // Enables swiftclick-ignore class support
NextInteraction.start();
